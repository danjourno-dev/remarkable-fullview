namespace Fullview.Infra.Configuration;

/// <summary>
/// Deploy-time settings resolved entirely from environment variables — never hardcoded,
/// so this public repo never commits Dan's AWS account id or alert email.
/// CDK_DEFAULT_ACCOUNT / CDK_DEFAULT_REGION are set automatically by the `cdk` CLI from
/// whichever AWS credentials are active (local profile, or the OIDC-assumed role in CI).
/// </summary>
public sealed record EnvironmentSettings(
    string ResourcePrefix,
    string? AwsAccountId,
    string AwsRegion,
    string AlertEmail)
{
    public static EnvironmentSettings FromEnvironment()
    {
        var account = Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT");
        var region = Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION") ?? "eu-west-2";
        var alertEmail = Environment.GetEnvironmentVariable("FULLVIEW_ALERT_EMAIL")
            ?? throw new InvalidOperationException(
                "FULLVIEW_ALERT_EMAIL must be set (budget/alarm notifications). " +
                "Locally: export it before `cdk deploy`/`cdk diff`. In CI it comes from the " +
                "FULLVIEW_ALERT_EMAIL repository variable.");

        return new EnvironmentSettings(
            ResourcePrefix: "fullview-",
            AwsAccountId: account,
            AwsRegion: region,
            AlertEmail: alertEmail);
    }
}
