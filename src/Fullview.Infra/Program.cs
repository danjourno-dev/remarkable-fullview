using Amazon.CDK;
using Fullview.Infra.Configuration;
using Fullview.Infra.Stacks;

var app = new App();
var settings = EnvironmentSettings.FromEnvironment();

_ = new FullviewStack(app, $"{settings.ResourcePrefix}stack", new StackProps
{
    Env = new Amazon.CDK.Environment
    {
        Account = settings.AwsAccountId,
        Region = settings.AwsRegion
    }
}, settings);

app.Synth();
