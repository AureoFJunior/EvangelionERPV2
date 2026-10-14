using Prometheus;

namespace EvangelionERPV2.Web.Security.Iam
{
    public static class IamMetrics
    {
        public static readonly Counter Denied = Metrics.CreateCounter(
            "evangelion_iam_denied_total",
            "Permission checks that did not pass, by why",
            new CounterConfiguration { LabelNames = ["reason"] });

        public static readonly Counter StepUps = Metrics.CreateCounter(
            "evangelion_iam_step_up_total",
            "MFA step-up attempts, by outcome",
            new CounterConfiguration { LabelNames = ["result"] });

        public static string ReasonOf(PermissionDecision decision) => decision switch
        {
            PermissionDecision.MfaRequired => "mfa",
            PermissionDecision.StepUpRequired => "step_up",
            _ => "denied"
        };
    }
}
