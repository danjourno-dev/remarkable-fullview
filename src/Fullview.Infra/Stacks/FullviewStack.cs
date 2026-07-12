using Amazon.CDK;
using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.Budgets;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Constructs;
using Fullview.Infra.Configuration;
using CloudFront = Amazon.CDK.AWS.CloudFront;
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

        // Single-user v1 auth (see docs/plans/implementation.md): one shared API key held as
        // a SecureString in SSM Parameter Store. CloudFormation can't create SecureString
        // parameters, so this stack never creates the parameter itself — Dan (or a forker)
        // creates it once by hand (a plan-sanctioned manual step, see docs/device-setup.md)
        // and this stack only ever reads it, via a REQUEST authorizer Lambda that checks the
        // `x-api-key` header against it.
        var apiKeyParameterName = $"/{settings.ResourcePrefix}api-key";
        var apiKeyParameterArn = $"arn:aws:ssm:{Region}:{Account}:parameter{apiKeyParameterName}";

        var authorizerFunction = new Function(this, "AuthorizerFunction", new FunctionProps
        {
            FunctionName = $"{settings.ResourcePrefix}authorizer",
            Runtime = Runtime.DOTNET_8,
            Handler = "Fullview.Api::Fullview.Api.Functions.AuthorizerFunction::FunctionHandler",
            Code = Code.FromAsset(ResolveLambdaAsset()),
            MemorySize = 256,
            Timeout = Duration.Seconds(10),
            Environment = new Dictionary<string, string>
            {
                ["FULLVIEW_API_KEY_PARAM"] = apiKeyParameterName
            }
        });

        authorizerFunction.AddToRolePolicy(new Amazon.CDK.AWS.IAM.PolicyStatement(new Amazon.CDK.AWS.IAM.PolicyStatementProps
        {
            Actions = new[] { "ssm:GetParameter" },
            Resources = new[] { apiKeyParameterArn }
        }));

        // The parameter uses the default AWS-managed key (alias/aws/ssm), whose key id isn't
        // known at synth time — scoping by kms:ViaService instead of a resource ARN is the
        // standard pattern for granting decrypt access to that key.
        authorizerFunction.AddToRolePolicy(new Amazon.CDK.AWS.IAM.PolicyStatement(new Amazon.CDK.AWS.IAM.PolicyStatementProps
        {
            Actions = new[] { "kms:Decrypt" },
            Resources = new[] { "*" },
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object> { ["kms:ViaService"] = $"ssm.{Region}.amazonaws.com" }
            }
        }));

        // Stage 6.5: Google Calendar puller. One EventBridge-scheduled Lambda sweeps every
        // calendar in the config list — no work-specific code path, Context comes only from
        // that config (see docs/plans/implementation.md's Stage 6.5 design rule). All three
        // parameters below are created by hand (checkpoints 6.5.1/6.5.2), same reasoning as
        // the API key above: CloudFormation can't create SecureString parameters, and the
        // calendar list is deliberately a config change, not a code change, so this stack
        // never seeds it either.
        var googleOAuthClientParamName = $"/{settings.ResourcePrefix}google-oauth-client";
        var googleRefreshTokenParamName = $"/{settings.ResourcePrefix}google-refresh-token";
        var googleCalendarsParamName = $"/{settings.ResourcePrefix}google-calendars";

        var calendarPullFunction = new Function(this, "CalendarPullFunction", new FunctionProps
        {
            FunctionName = $"{settings.ResourcePrefix}calendar-pull",
            Runtime = Runtime.DOTNET_8,
            Handler = "Fullview.Api::Fullview.Api.Functions.CalendarPullFunction::FunctionHandler",
            Code = Code.FromAsset(ResolveLambdaAsset()),
            MemorySize = 256,
            // Google's API can be slow across many calendars/pages; the default 3s
            // API Gateway-oriented timeout used by the other functions doesn't apply here
            // since nothing waits on this one synchronously.
            Timeout = Duration.Seconds(60),
            Environment = new Dictionary<string, string>
            {
                ["FULLVIEW_TABLE_NAME"] = table.TableName,
                ["FULLVIEW_GOOGLE_OAUTH_CLIENT_PARAM"] = googleOAuthClientParamName,
                ["FULLVIEW_GOOGLE_REFRESH_TOKEN_PARAM"] = googleRefreshTokenParamName,
                ["FULLVIEW_GOOGLE_CALENDARS_PARAM"] = googleCalendarsParamName
            }
        });
        table.GrantReadWriteData(calendarPullFunction);

        calendarPullFunction.AddToRolePolicy(new Amazon.CDK.AWS.IAM.PolicyStatement(new Amazon.CDK.AWS.IAM.PolicyStatementProps
        {
            Actions = new[] { "ssm:GetParameter" },
            Resources = new[]
            {
                $"arn:aws:ssm:{Region}:{Account}:parameter{googleOAuthClientParamName}",
                $"arn:aws:ssm:{Region}:{Account}:parameter{googleRefreshTokenParamName}",
                $"arn:aws:ssm:{Region}:{Account}:parameter{googleCalendarsParamName}"
            }
        }));

        calendarPullFunction.AddToRolePolicy(new Amazon.CDK.AWS.IAM.PolicyStatement(new Amazon.CDK.AWS.IAM.PolicyStatementProps
        {
            Actions = new[] { "kms:Decrypt" },
            Resources = new[] { "*" },
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object> { ["kms:ViaService"] = $"ssm.{Region}.amazonaws.com" }
            }
        }));

        var calendarPullSchedule = new Rule(this, "CalendarPullSchedule", new RuleProps
        {
            RuleName = $"{settings.ResourcePrefix}calendar-pull",
            Schedule = Schedule.Rate(Duration.Minutes(15))
        });
        calendarPullSchedule.AddTarget(new LambdaFunction(calendarPullFunction));

        // L1 (Cfn*) constructs throughout: the .NET binding for the HTTP API L2's Lambda
        // integration helper (Amazon.CDK.AWS.Apigatewayv2.Integrations) never went past a
        // 2020-era alpha release, so it's not a viable dependency for a public repo template.
        // Stage 6: Fullview.Web calls /sync directly from the browser (same shape as the
        // device, per B5), so the HTTP API needs CORS. AllowOrigins is "*" rather than
        // pinned to the CloudFront domain below because that domain isn't known until
        // after this same stack's first deploy (chicken-and-egg) — acceptable here since
        // the endpoint is protected by the x-api-key authorizer, not cookies/credentials.
        var httpApi = new CfnApi(this, "HttpApi", new CfnApiProps
        {
            Name = $"{settings.ResourcePrefix}api",
            ProtocolType = "HTTP",
            CorsConfiguration = new CfnApi.CorsProperty
            {
                AllowOrigins = new[] { "*" },
                AllowMethods = new[] { "GET", "POST", "OPTIONS" },
                AllowHeaders = new[] { "content-type", "x-api-key" },
                MaxAge = 300
            }
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

        // /health stays open (no sensitive data, useful as an unauthenticated liveness probe);
        // /sync carries the actual data and requires the shared API key.
        var apiKeyAuthorizer = new CfnAuthorizer(this, "ApiKeyAuthorizer", new CfnAuthorizerProps
        {
            ApiId = httpApi.Ref,
            Name = "ApiKeyAuthorizer",
            AuthorizerType = "REQUEST",
            AuthorizerUri = $"arn:aws:apigateway:{Region}:lambda:path/2015-03-31/functions/{authorizerFunction.FunctionArn}/invocations",
            AuthorizerPayloadFormatVersion = "2.0",
            EnableSimpleResponses = true,
            IdentitySource = new[] { "$request.header.x-api-key" },
            AuthorizerResultTtlInSeconds = 300
        });

        authorizerFunction.AddPermission("ApiGatewayInvoke", new Permission
        {
            Principal = new Amazon.CDK.AWS.IAM.ServicePrincipal("apigateway.amazonaws.com"),
            Action = "lambda:InvokeFunction",
            SourceArn = $"arn:aws:execute-api:{Region}:{Account}:{httpApi.Ref}/authorizers/{apiKeyAuthorizer.Ref}"
        });

        _ = new CfnRoute(this, "SyncRoute", new CfnRouteProps
        {
            ApiId = httpApi.Ref,
            RouteKey = "POST /sync",
            Target = $"integrations/{syncIntegration.Ref}",
            AuthorizationType = "CUSTOM",
            AuthorizerId = apiKeyAuthorizer.Ref
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

        // Stage 6: Fullview.Web static hosting. Private bucket, no static-website-hosting
        // mode — CloudFront reaches it via Origin Access Control, never directly.
        // `cd-web.yml` builds `dist/` and `aws s3 sync`s it here on every push to main that
        // touches Fullview.Web; this stack only owns the bucket/distribution, not the
        // deploy — that keeps a web-only change from needing a full `cdk deploy`.
        var webBucket = new Bucket(this, "WebBucket", new BucketProps
        {
            BucketName = settings.AwsAccountId is not null
                ? $"{settings.ResourcePrefix}web-{settings.AwsAccountId}-{settings.AwsRegion}"
                : null,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        var webDistribution = new CloudFront.Distribution(this, "WebDistribution", new CloudFront.DistributionProps
        {
            Comment = $"{settings.ResourcePrefix}web",
            DefaultRootObject = "index.html",
            DefaultBehavior = new CloudFront.BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(webBucket),
                ViewerProtocolPolicy = CloudFront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS
            },
            // react-router uses browser history, not hash routing — CloudFront must serve
            // index.html for any path (S3 itself 403s/404s on unknown keys) so a deep-link
            // or refresh on e.g. /recipes doesn't break.
            ErrorResponses = new[]
            {
                new CloudFront.ErrorResponse
                {
                    HttpStatus = 403,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html"
                },
                new CloudFront.ErrorResponse
                {
                    HttpStatus = 404,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html"
                }
            }
        });

        _ = new CfnOutput(this, "WebBucketName", new CfnOutputProps
        {
            Value = webBucket.BucketName,
            Description = "cd-web.yml syncs Fullview.Web's dist/ here"
        });

        _ = new CfnOutput(this, "WebDistributionId", new CfnOutputProps
        {
            Value = webDistribution.DistributionId,
            Description = "cd-web.yml invalidates this distribution's cache after each sync"
        });

        _ = new CfnOutput(this, "WebUrl", new CfnOutputProps
        {
            Value = $"https://{webDistribution.DistributionDomainName}",
            Description = "Fullview.Web — set as VITE_API_BASE_URL's counterpart in the SPA's own config"
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

        var authorizerErrorAlarm = new Alarm(this, "AuthorizerFunctionErrorAlarm", new AlarmProps
        {
            AlarmName = $"{settings.ResourcePrefix}authorizer-errors",
            AlarmDescription = "Authorizer Lambda errored — likely can't read the API key from SSM; /sync will fail closed for everyone.",
            Metric = authorizerFunction.MetricErrors(new MetricOptions
            {
                Period = Duration.Minutes(5),
                Statistic = Stats.SUM
            }),
            Threshold = 1,
            EvaluationPeriods = 1,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING
        });
        authorizerErrorAlarm.AddAlarmAction(new SnsAction(alertTopic));

        var calendarPullErrorAlarm = new Alarm(this, "CalendarPullFunctionErrorAlarm", new AlarmProps
        {
            AlarmName = $"{settings.ResourcePrefix}calendar-pull-errors",
            AlarmDescription = "Calendar pull Lambda errored — Google credentials, calendar config, or the API itself may need attention.",
            Metric = calendarPullFunction.MetricErrors(new MetricOptions
            {
                Period = Duration.Minutes(15),
                Statistic = Stats.SUM
            }),
            Threshold = 1,
            EvaluationPeriods = 1,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING
        });
        calendarPullErrorAlarm.AddAlarmAction(new SnsAction(alertTopic));

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
