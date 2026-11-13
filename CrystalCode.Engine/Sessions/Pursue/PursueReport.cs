using System.Globalization;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Sessions.Transcripts;

namespace CrystalCode.Engine.Sessions.Pursue;

internal static class PursueReport
{
    public static TranscriptDocument Build(PursueState? state, PursueConfiguration configuration)
    {
        List<IReadOnlyList<string>> rows =
        [
            ["Status", state is null ? "No objective" : DisplayCase.Token(PursueState.StatusToken(state.Status))]
        ];
        if (state is not null)
        {
            rows.Add(["Objective", state.Objective]);
            if (state.Phase != PursuePhase.None)
            {
                rows.Add(["Phase", DisplayCase.Token(PursueState.PhaseToken(state.Phase))]);
            }

            if (state.Reason.Length > 0)
            {
                rows.Add(["Reason", state.Reason]);
            }

            if (state.NextStep.Length > 0)
            {
                rows.Add(["Next step", state.NextStep]);
            }

            rows.Add(["Checks this run", Count(state.Checks)]);
            rows.Add(["Continuations used", Count(state.Continuations)]);
            rows.Add(["Stalls this run", Count(state.Stalls)]);
        }

        var model = configuration.Model;
        rows.Add(["Checker model", model.UsesSessionModel ? "Session model" : model.Provider + "/" + model.Model]);
        rows.Add(["Checker thinking", ThinkingLabel.For(model.ThinkingEffort)]);
        if (!model.UsesSessionModel)
        {
            rows.Add(["Checker tier", model.ServiceTier ?? "Unset"]);
            rows.Add(["Checker speed", model.Speed ?? "Unset"]);
            rows.Add(["Checker context", model.ContextWindow is int window ? ContextWindowSelection.Format(window) : "Model default"]);
        }

        var compaction = model.Compaction;
        rows.Add(["Compaction model", compaction.HasSelection ? compaction.Provider + "/" + compaction.Model : "Session setting, then checker model"]);
        if (compaction.HasSelection)
        {
            rows.Add(["Compaction thinking", ThinkingLabel.For(compaction.ThinkingEffort)]);
            rows.Add(["Compaction tier", compaction.ServiceTier ?? "Unset"]);
            rows.Add(["Compaction speed", compaction.Speed ?? "Unset"]);
            rows.Add(["Compaction context", compaction.ContextWindow is int window ? ContextWindowSelection.Format(window) : "Model default"]);
        }

        var limits = configuration.Limits;
        return new TranscriptDocument("Pursue",
        [
            new("keyValues", [], rows, [], 0),
            new("keyValues", [],
            [
                ["Continuation limit", Count(limits.MaximumContinuations)],
                ["Stall limit", Count(limits.MaximumStalls)],
                ["Model call limit", Count(limits.MaximumModelCalls)],
                ["Tool call limit", Count(limits.MaximumToolCalls)],
                ["Duration limit", Duration(limits.MaximumDurationSeconds)],
                ["Wait limit", Duration(limits.MaximumWaitSeconds)]
            ], [], 0)
        ]);
    }

    private static string Count(int? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "Unlimited";

    private static string Duration(int? seconds)
    {
        if (seconds is null)
        {
            return "Unlimited";
        }

        var duration = TimeSpan.FromSeconds(seconds.Value);
        var parts = new List<string>();
        Add(duration.Days, "day");
        Add(duration.Hours, "hour");
        Add(duration.Minutes, "minute");
        Add(duration.Seconds, "second");
        return parts.Count == 0 ? "0 seconds" : string.Join(' ', parts);

        void Add(int value, string unit)
        {
            if (value > 0)
            {
                parts.Add(value.ToString(CultureInfo.InvariantCulture) + " " + unit + (value == 1 ? string.Empty : "s"));
            }
        }
    }
}
