using Amazon.CDK;
using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.Budgets;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Constructs;
using Fullview.Infra.Configuration;
using DynamoAttribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace Fullview.Infra.Stacks;

/// <summary>
/// Stage 1: DynamoDB table, S3 inbox bucket, HTTP API + placeholder /health Lambda,
/// budget + alarms. Single-table layout and entity shapes arrive in Stage 2 — this
/// stack only lays the table down (PK/SK per B5), it doesn't model entities yet.
/// </summary>
public sealed class FullviewStack : Stack
{
    public FullviewStack(Construct scope, string id, IStackProps props, EnvironmentSettings settings)
        : base(scope, id, props)
    {
        Amazon.CDK.Tags.Of(this).Add("Project", "remarkable-fullview");

        var alertTopic = new Topic(this, "AlertTopic", new TopicProps
        {
            TopicName = $"{settings.ResourcePrefix}alerts",
            DisplayName = "remarkable-fullview alerts"
        });
        alertTopic.AddSubscription(new EmailSubscription(settings.AlertEmail));

        var table = new Table(this, "AppTable", new TableProps
        {
            TableName = $"{settings.ResourcePrefix}app",
            PartitionKey = new DynamoAttribute { Name = "pk", Type = AttributeType.STRING },
            SortKey = new DynamoAttribute { Name = "sk", Type = AttributeType.STRING },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = true
            },
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        // Stage 2: delta query for `/sync` reads everything changed since a cursor, ordered
        // by UpdatedAt (B5). Single-user v1 means gsi1pk is the same constant as pk — this
        // GSI exists purely to get an UpdatedAt-sorted view, not to shard by user.
        table.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "gsi1",
            PartitionKey = new DynamoAttribute { Name = "gsi1pk", Type = AttributeType.STRING },
            SortKey = new DynamoAttribute { Name = "gsi1sk", Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });

        var inboxBucket = new Bucket(this, "InboxBucket", new BucketProps
        {
            BucketName = settings.AwsAccountId is not null
                ? $"{settings.ResourcePrefix}inbox-{settings.AwsAccountId}-{settings.AwsRegion}"
                : null,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        var healthFunction = new Function(this, "HealthFunction", new FunctionProps
        {
            FunctionName = $"{settings.ResourcePrefix}health",
            Runtime = Runtime.DOTNET_8,
            Handler = "Fullview.Api::Fullview.Api.Functions.HealthFunction::FunctionHandler",
            Code = Code.FromAsset(ResolveLambdaAsset()),
            MemorySize = 256,
            Timeout = Duration.Seconds(10)
        });

        // Stage 2: same Fullview.Api package as HealthFunction — only the handler string
        // and environment differ.
        var syncFunction = new Function(this, "SyncFunction", new FunctionProps
        {
            FunctionName = $"{settings.ResourcePrefix}sync",
            Runtime = Runtime.DOTNET_8,
            Handler = "Fullview.Api::Fullview.Api.Functions.SyncFunction::FunctionHandler",
            Code = Code.FromAsset(ResolveLambdaAsset()),
            MemorySize = 256,
            Timeout = Duration.Seconds(10),
            Environment = new Dictionary<string, string>
            {
                ["FULLVIEW_TABLE_NAME"] = table.TableName
            }
        });
        table.GrantReadWriteData(syncFunction);

        // L1 (Cfn*) constructs throughout: the .NET binding for the HTTP API L2's Lambda
        // integration helper (Amazon.CDK.AWS.Apigatewayv2.Integrations) never went past a
        // 2020-era alpha release, so it's not a viable dependency for a public repo template.
        var httpApi = new CfnApi(this, "HttpApi", new CfnApiProps
        {
            Name = $"{settings.ResourcePrefix}api",
            ProtocolType = "HTTP"
        });

        var healthIntegration = new CfnIntegration(this, "HealthIntegration", new CfnIntegrationProps
        {
            ApiId = httpApi.Ref,
            IntegrationType = "AWS_PROXY",
            IntegrationUri = healthFunction.FunctionArn,
            IntegrationMethod = "POST",
            PayloadFormatVersion = "2.0"
        });

        _ = new CfnRoute(this, "HealthRoute", new CfnRouteProps
        {
            ApiId = httpApi.Ref,
            RouteKey = "GET /health",
            Target = $"integrations/{healthIntegration.Ref}"
        });

        var syncIntegration = new CfnIntegration(this, "SyncIntegration", new CfnIntegrationProps
        {
            ApiId = httpApi.Ref,
            IntegrationType = "AWS_PROXY",
            IntegrationUri = syncFunction.FunctionArn,
            IntegrationMethod = "POST",
            PayloadFormatVersion = "2.0"
        });

        _ = new CfnRoute(this, "SyncRoute", new CfnRouteProps
        {
            ApiId = httpApi.Ref,
            RouteKey = "POST /sync",
            Target = $"integrations/{syncIntegration.Ref}"
        });

        _ = new CfnStage(this, "DefaultStage", new CfnStageProps
        {
            ApiId = httpApi.Ref,
            StageName = "$default",
            AutoDeploy = true
        });

        healthFunction.AddPermission("ApiGatewayInvoke", new Permission
        {
            Principal = new Amazon.CDK.AWS.IAM.ServicePrincipal("apigateway.amazonaws.com"),
            Action = "lambda:InvokeFunction",
            SourceArn = $"arn:aws:execute-api:{Region}:{Account}:{httpApi.Ref}/*/*/health"
        });

        syncFunction.AddPermission("ApiGatewayInvoke", new Permission
        {
            Principal = new Amazon.CDK.AWS.IAM.ServicePrincipal("apigateway.amazonaws.com"),
            Action = "lambda:InvokeFunction",
            SourceArn = $"arn:aws:execute-api:{Region}:{Account}:{httpApi.Ref}/*/*/sync"
        });

        _ = new CfnOutput(this, "HttpApiUrl", new CfnOutputProps
        {
            Value = $"https://{httpApi.Ref}.execute-api.{Region}.amazonaws.com",
            Description = "Base URL — append /health for the Stage 1 checkpoint"
        });

        var errorAlarm = new Alarm(this, "HealthFunctionErrorAlarm", new AlarmProps
        {
            AlarmName = $"{settings.ResourcePrefix}health-errors",
            AlarmDescription = "Health Lambda errored — deploy or runtime problem.",
            Metric = healthFunction.MetricErrors(new MetricOptions
            {
                Period = Duration.Minutes(5),
                Statistic = Stats.SUM
            }),
            Threshold = 1,
            EvaluationPeriods = 1,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING
        });
        errorAlarm.AddAlarmAction(new SnsAction(alertTopic));

        var syncErrorAlarm = new Alarm(this, "SyncFunctionErrorAlarm", new AlarmProps
        {
            AlarmName = $"{settings.ResourcePrefix}sync-errors",
            AlarmDescription = "Sync Lambda errored — check for a bad mutation or a DynamoDB problem.",
            Metric = syncFunction.MetricErrors(new MetricOptions
            {
                Period = Duration.Minutes(5),
                Statistic = Stats.SUM
            }),
            Threshold = 1,
            EvaluationPeriods = 1,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING
        });
        syncErrorAlarm.AddAlarmAction(new SnsAction(alertTopic));

        _ = new CfnBudget(this, "MonthlyBudget", new CfnBudgetProps
        {
            Budget = new CfnBudget.BudgetDataProperty
            {
                BudgetType = "COST",
                TimeUnit = "MONTHLY",
                BudgetName = $"{settings.ResourcePrefix}monthly",
                // AWS Budgets ties BudgetLimit.Unit to the account's billing currency —
                // this account bills in USD, so GBP is rejected even though the plan's
                // "£10/mo" guardrail is expressed in GBP. ~£10 at time of writing.
                BudgetLimit = new CfnBudget.SpendProperty { Amount = 12, Unit = "USD" }
            },
            NotificationsWithSubscribers = new object[]
            {
                new CfnBudget.NotificationWithSubscribersProperty
                {
                    Notification = new CfnBudget.NotificationProperty
                    {
                        NotificationType = "ACTUAL",
                        ComparisonOperator = "GREATER_THAN",
                        Threshold = 80,
                        ThresholdType = "PERCENTAGE"
                    },
                    Subscribers = new object[]
                    {
                        new CfnBudget.SubscriberProperty
                        {
                            SubscriptionType = "EMAIL",
                            Address = settings.AlertEmail
                        }
                    }
                },
                new CfnBudget.NotificationWithSubscribersProperty
                {
                    Notification = new CfnBudget.NotificationProperty
                    {
                        NotificationType = "FORECASTED",
                        ComparisonOperator = "GREATER_THAN",
                        Threshold = 100,
                        ThresholdType = "PERCENTAGE"
                    },
                    Subscribers = new object[]
                    {
                        new CfnBudget.SubscriberProperty
                        {
                            SubscriptionType = "EMAIL",
                            Address = settings.AlertEmail
                        }
                    }
                }
            }
        });

        _ = table;
        _ = inboxBucket;
    }

    /// <summary>
    /// Points at the zip built by `dotnet lambda package` in CI (see cd-infra.yml) via
    /// LAMBDA_PACKAGE_PATH. Without it, falls back to the raw source directory so
    /// `cdk synth`/`cdk diff` still work locally — that fallback is NOT a real deployable
    /// package (no compiled build), so a real `cdk deploy` always needs LAMBDA_PACKAGE_PATH
    /// set to a `dotnet lambda package` zip first.
    /// </summary>
    private static string ResolveLambdaAsset()
    {
        var packagePath = System.Environment.GetEnvironmentVariable("LAMBDA_PACKAGE_PATH");
        if (!string.IsNullOrEmpty(packagePath))
        {
            return packagePath;
        }

        return "../Fullview.Api";
    }
}
