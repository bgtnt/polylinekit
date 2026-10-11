using System.Globalization;
using PolylineKit;

namespace PolylineKit.IndependentRegions;

internal sealed record Failure(string Stage, string Input, string Reason, string? Actual = null, string? Reference = null);
internal sealed record OriginalInvalidCoreObservation(string Role, string Id, string FillRule, string NtsInvalidReason,
    bool CoreStructureAccepted, bool FactorySucceeded, bool FilledAreaInvoked, double? AreaM2, string? RawArea,
    string? ExceptionType, string? Exception, string Scope);
internal sealed class ErrorSummary(string backend, string rule, string direction, string metric, double budget)
{
    public string Backend { get; } = backend;
    public string FillRule { get; } = rule;
    public string Direction { get; } = direction;
    public string Metric { get; } = metric;
    public double Budget { get; } = budget;
    public int Checks { get; private set; }
    public int NonfiniteChecks { get; private set; }
    public int DomainViolations { get; private set; }
    public int DomainBudgetViolations { get; private set; }
    public int ZeroValues { get; private set; }
    public int PositiveValues { get; private set; }
    public int NegativeValues { get; private set; }
    public int AboveOneValues { get; private set; }
    public double MaximumAbsoluteError { get; private set; }
    public string? WorstInput { get; private set; }
    public double? ActualAtWorst { get; private set; }
    public double? ReferenceAtWorst { get; private set; }
    public double? MinimumActual { get; private set; }
    public double? MaximumActual { get; private set; }
    internal bool Add(string input, double actual, double reference)
    {
        Checks++;
        if (!double.IsFinite(actual) || !double.IsFinite(reference)) { NonfiniteChecks++; return false; }
        MinimumActual = MinimumActual.HasValue ? Math.Min(MinimumActual.Value, actual) : actual;
        MaximumActual = MaximumActual.HasValue ? Math.Max(MaximumActual.Value, actual) : actual;
        if (actual == 0) ZeroValues++;
        else if (actual > 0) PositiveValues++;
        else NegativeValues++;
        bool ratio = Metric is "Coverage" or "IoU";
        if (ratio && actual > 1) AboveOneValues++;
        bool rawDomain = actual >= 0 && reference >= 0 && (!ratio || actual <= 1 && reference <= 1);
        if (!rawDomain) DomainViolations++;
        bool domain = actual >= -Budget && reference >= -Budget && (!ratio || actual <= 1 + Budget && reference <= 1 + Budget);
        if (!domain) DomainBudgetViolations++;
        double error = Math.Abs(actual - reference);
        if (WorstInput is null || error > MaximumAbsoluteError)
        {
            MaximumAbsoluteError = error; WorstInput = input; ActualAtWorst = actual; ReferenceAtWorst = reference;
        }
        return domain && error <= Budget;
    }
}

internal sealed record ValidationReport(bool Passed, int SourceZones, int SourceQueries, int EligibleZones, int EligibleQueries,
    int ExcludedOriginalInputs, InputIssue[] InputIssues, OriginalInvalidCoreObservation[] OriginalInvalidCoreObservations,
    bool ComparisonsUseOnlyValidOriginalTopology, int PairsPerDirection, int BoundingBoxCandidatesPerDirection,
    int QuantizedBoundsMembershipChanges, int SelectedIntersectionCalls, int CoreSelectedApiCalls, int FullCompareCalls, int FullPairs,
    string[] DisjointFullPairIds, int Assertions, int FailureCount, int OmittedFailureDetails, Failure[] Failures, ErrorSummary[] Errors);

internal static class Validation
{
    internal static ValidationReport Check(FrozenCorpus corpus)
    {
        var errors = new Dictionary<(string Backend, string Rule, string Direction, string Metric), ErrorSummary>();
        var failures = new List<Failure>(); int failureCount = 0, assertions = 0, selectedCalls = 0, coreSelectedApiCalls = 0, fullCalls = 0;
        void Fail(Failure failure)
        {
            failureCount++;
            if (failures.Count < 512) failures.Add(failure);
        }
        foreach (InputIssue issue in corpus.InvalidInputs.Where(i => i.Stage == "quantized-NTS"))
            Fail(new(issue.Stage, issue.Role + "/" + issue.Id, issue.Reason));
        var invalidCoreObservations = new List<OriginalInvalidCoreObservation>();
        foreach (InputIssue issue in corpus.InvalidInputs.Where(i => i.Stage == "original-NTS"))
        {
            RawFeature source = (issue.Role == "zones" ? corpus.Zones.Source : corpus.Queries.Source).Single(f => f.Id == issue.Id);
            bool acceptsStructure = source.Rings.All(AcceptsCoreStructure);
            foreach (PathFillRule rule in Contracts.Rules)
            {
                bool prepared = false, invoked = false;
                try
                {
                    PreparedRegion region = Session.PrepareCore(source); prepared = true;
                    invoked = true;
                    double area = RegionArea.FilledArea(region, rule);
                    invalidCoreObservations.Add(new(issue.Role, issue.Id, rule.ToString(), issue.Reason, acceptsStructure, prepared,
                        invoked, double.IsFinite(area) ? area : null, area.ToString("R", CultureInfo.InvariantCulture), null, null,
                        "Unreferenced fill-rule observation of original NTS-invalid topology; no NTS/Clipper accuracy comparison."));
                    if (!double.IsFinite(area) || area < -Contracts.AreaBudget)
                        Fail(new("Core-unreferenced-original-invalid/" + rule, issue.Role + "/" + issue.Id,
                            "Nonfinite area or negative area beyond the fixed rounding budget on structurally accepted input",
                            area.ToString("R", CultureInfo.InvariantCulture)));
                }
                catch (Exception error)
                {
                    invalidCoreObservations.Add(new(issue.Role, issue.Id, rule.ToString(), issue.Reason, acceptsStructure, prepared,
                        invoked, null, null, error.GetType().FullName, error.ToString(),
                        "Unreferenced fill-rule attempt on original NTS-invalid topology; documented structural rejection is distinct from numerical failure."));
                    if (acceptsStructure || error is not ArgumentException)
                        Fail(new("Core-unreferenced-original-invalid/" + rule, issue.Role + "/" + issue.Id,
                            "Core rejected structurally accepted raw input: " + error));
                }
            }
        }
        var sessions = new Dictionary<(string Backend, PathFillRule Rule, bool Reverse), Session>();
        foreach (string backend in Session.Backends)
        foreach (PathFillRule rule in Contracts.Rules)
        foreach (bool reverse in new[] { false, true })
            sessions.Add((backend, rule, reverse), new(reverse ? corpus.Queries.Eligible : corpus.Zones.Eligible,
                reverse ? corpus.Zones.Eligible : corpus.Queries.Eligible, backend, rule));
        Session reference = sessions[(Session.Backends[2], PathFillRule.NonZero, false)];
        void Compare(string backend, PathFillRule rule, bool reverse, string metric, string input, double actual, double expected,
            bool fraction = false)
        {
            assertions++;
            string direction = reverse ? "queries-to-zones" : "zones-to-queries";
            var key = (backend, rule.ToString(), direction, metric);
            if (!errors.TryGetValue(key, out ErrorSummary? summary))
            {
                summary = new(backend, rule.ToString(), direction, metric, fraction ? Contracts.FractionBudget : Contracts.AreaBudget);
                errors.Add(key, summary);
            }
            if (!summary.Add(input, actual, expected))
                Fail(new(backend + "/" + rule + "/" + direction + "/" + metric, input,
                    !double.IsFinite(actual) || !double.IsFinite(expected) ? "Nonfinite metric" :
                    actual < -summary.Budget || expected < -summary.Budget || fraction && (actual > 1 + summary.Budget || expected > 1 + summary.Budget) ?
                    "Metric exceeds the predeclared rounding budget outside its nonnegative area or [0,1] fraction domain" :
                    "Difference exceeds the predeclared budget",
                    actual.ToString("R", CultureInfo.InvariantCulture), expected.ToString("R", CultureInfo.InvariantCulture)));
        }
        void CompareIou(string backend, PathFillRule rule, bool reverse, string input, double? actual, double? expected)
        {
            if (actual.HasValue && expected.HasValue) Compare(backend, rule, reverse, "IoU", input, actual.Value, expected.Value, true);
            else
            {
                assertions++;
                if (actual.HasValue != expected.HasValue)
                    Fail(new(backend + "/" + rule + "/IoU", input, "Defined/undefined ratio contract differs",
                        actual?.ToString("R", CultureInfo.InvariantCulture) ?? "undefined", expected?.ToString("R", CultureInfo.InvariantCulture) ?? "undefined"));
            }
        }
        foreach (var (key, session) in sessions)
        {
            PreparedOperand[] expectedFirst = key.Reverse ? reference.Second : reference.First;
            PreparedOperand[] expectedSecond = key.Reverse ? reference.First : reference.Second;
            foreach (var (operand, expected) in session.First.Zip(expectedFirst).Concat(session.Second.Zip(expectedSecond)))
                Compare(key.Backend, key.Rule, key.Reverse, "OwnAreaM2", operand.Raw.Id, operand.Area, expected.Area);
        }
        var disjoint = (from a in corpus.Zones.Eligible
                        from b in corpus.Queries.Eligible
                        where !a.Bounds.Overlaps(b.Bounds)
                        select (a.Id, OtherId: b.Id)).OrderBy(p => p.Id, StringComparer.Ordinal).ThenBy(p => p.OtherId, StringComparer.Ordinal)
                        .Take(64).Select(p => p.Id + "/" + p.OtherId).ToHashSet(StringComparer.Ordinal);
        Contracts.Require(disjoint.Count == 64, "Expected at least 64 disjoint pairs for the fixed full-metric sample.");
        int pairs = 0, fullPairs = 0, nativeMembershipChanges = 0;
        var selectedValues = new Dictionary<(string Backend, PathFillRule Rule, bool Reverse), double>();
        Session clipReference = sessions[(Session.Backends[1], PathFillRule.NonZero, false)];
        for (int z = 0; z < reference.First.Length; z++)
        for (int q = 0; q < reference.Second.Length; q++)
        {
            selectedValues.Clear();
            string input = reference.First[z].Raw.Id + "/" + reference.Second[q].Raw.Id;
            bool candidate = reference.First[z].Raw.Bounds.Overlaps(reference.Second[q].Raw.Bounds);
            if (candidate != clipReference.First[z].NativeBounds.Overlaps(clipReference.Second[q].NativeBounds)) nativeMembershipChanges++;
            double expectedIntersection;
            try { expectedIntersection = reference.Intersection(reference.First[z], reference.Second[q]); }
            catch (Exception error)
            {
                Fail(new("NTS-reference-intersection", input, error.ToString()));
                pairs++; continue;
            }
            foreach (var (key, session) in sessions)
            {
                PreparedOperand first = key.Reverse ? session.First[q] : session.First[z];
                PreparedOperand second = key.Reverse ? session.Second[z] : session.Second[q];
                try
                {
                    if (key.Backend == Session.Backends[0]) coreSelectedApiCalls++;
                    double actual = session.SelectedForValidation(first, second); selectedCalls++;
                    selectedValues.Add(key, actual);
                    Compare(key.Backend, key.Rule, key.Reverse, "SelectedIntersectionM2", input, actual, expectedIntersection);
                    Compare(key.Backend, key.Rule, key.Reverse, "Coverage", input, actual / first.Area,
                        expectedIntersection / (key.Reverse ? reference.Second[q].Area : reference.First[z].Area), true);
                }
                catch (Exception error) { Fail(new(key.Backend + "/" + key.Rule + "/selected", input, error.ToString())); }
            }
            if (candidate || disjoint.Contains(input))
            {
                MetricValue expected;
                try { expected = reference.Full(reference.First[z], reference.Second[q]); }
                catch (Exception error)
                {
                    Fail(new("NTS-reference-full", input, error.ToString()));
                    pairs++; continue;
                }
                fullPairs++;
                foreach (var (key, session) in sessions)
                {
                    PreparedOperand first = key.Reverse ? session.First[q] : session.First[z];
                    PreparedOperand second = key.Reverse ? session.Second[z] : session.Second[q];
                    try
                    {
                        MetricValue actual = session.Full(first, second); fullCalls++;
                        Compare(key.Backend, key.Rule, key.Reverse, "FirstAreaM2", input, actual.First, key.Reverse ? expected.Second : expected.First);
                        Compare(key.Backend, key.Rule, key.Reverse, "SecondAreaM2", input, actual.Second, key.Reverse ? expected.First : expected.Second);
                        Compare(key.Backend, key.Rule, key.Reverse, "FullIntersectionM2", input, actual.Intersection, expected.Intersection);
                        if (selectedValues.TryGetValue(key, out double selected))
                            Compare(key.Backend, key.Rule, key.Reverse, "SelectedVsFullIntersectionM2", input, selected, actual.Intersection);
                        if (double.IsFinite(actual.Intersection) && double.IsFinite(actual.First) && double.IsFinite(actual.Second) &&
                            actual.Intersection > Math.Min(actual.First, actual.Second) + Contracts.AreaBudget)
                            Fail(new(key.Backend + "/" + key.Rule + "/full", input, "Intersection exceeds either operand area beyond the fixed area budget",
                                actual.Intersection.ToString("R", CultureInfo.InvariantCulture), Math.Min(actual.First, actual.Second).ToString("R", CultureInfo.InvariantCulture)));
                        Compare(key.Backend, key.Rule, key.Reverse, "UnionM2", input, actual.Union, expected.Union);
                        Compare(key.Backend, key.Rule, key.Reverse, "SymmetricDifferenceM2", input, actual.Xor, expected.Xor);
                        CompareIou(key.Backend, key.Rule, key.Reverse, input, actual.Iou, expected.Iou);
                    }
                    catch (Exception error) { Fail(new(key.Backend + "/" + key.Rule + "/full", input, error.ToString())); }
                }
            }
            pairs++;
            if (pairs % 1000 == 0) Console.WriteLine($"Independent-region verification: {pairs}/{corpus.EligiblePairs} pairs.");
        }
        Contracts.Require(pairs == corpus.EligiblePairs, "Incomplete selected pair inventory.");
        Corpus.VerifyUnchanged(corpus);
        return new(failureCount == 0, corpus.Zones.Source.Length, corpus.Queries.Source.Length, corpus.Zones.Eligible.Length,
            corpus.Queries.Eligible.Length, corpus.InvalidInputs.Count(i => i.Stage == "original-NTS"), corpus.InvalidInputs,
            invalidCoreObservations.ToArray(), true,
            pairs, corpus.EligibleCandidates, nativeMembershipChanges, selectedCalls, coreSelectedApiCalls, fullCalls, fullPairs,
            disjoint.Order(StringComparer.Ordinal).ToArray(), assertions, failureCount, Math.Max(0, failureCount - failures.Count),
            failures.ToArray(), errors.Values.OrderBy(e => e.Backend, StringComparer.Ordinal).ThenBy(e => e.FillRule, StringComparer.Ordinal)
                .ThenBy(e => e.Direction, StringComparer.Ordinal).ThenBy(e => e.Metric, StringComparer.Ordinal).ToArray());
    }

    private static bool AcceptsCoreStructure(Point2[] ring)
    {
        var cleaned = new List<Point2>();
        foreach (Point2 point in ring)
            if (cleaned.Count == 0 || cleaned[^1].X != point.X || cleaned[^1].Y != point.Y) cleaned.Add(point);
        if (cleaned.Count > 1 && cleaned[0].X == cleaned[^1].X && cleaned[0].Y == cleaned[^1].Y) cleaned.RemoveAt(cleaned.Count - 1);
        return cleaned.Count >= 3;
    }

    internal static object AnalyticPackageCheck()
    {
        Point2[] Square(double x, double y, double width, double height) =>
            [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height), new(x, y)];
        var first = new RawFeature("shell-hole", "Analytic shell and same-direction hole", [new(Square(0, 0, 10, 10), [Square(2, 2, 4, 4)])]);
        var second = new RawFeature("query", "Analytic query", [new(Square(4, 0, 8, 8), [])]);
        var rows = new List<object>(); int checks = 0;
        foreach (string backend in Session.Backends)
        foreach (PathFillRule rule in Contracts.Rules)
        {
            var session = new Session([first], [second], backend, rule);
            MetricValue value = session.Full(session.First[0], session.Second[0]);
            foreach (var (actual, expected) in new[] { (value.First, 84), (value.Second, 64), (value.Intersection, 40), (value.Union, 108), (value.Xor, 68) })
            {
                Contracts.Require(Math.Abs(actual - expected) <= 1e-12, "Analytic package check failed: " + backend + "/" + rule);
                checks++;
            }
            Contracts.Require(value.Iou.HasValue && Math.Abs(value.Iou.Value - 10.0 / 27) <= 1e-12, "Analytic IoU failed."); checks++;
            rows.Add(new { Backend = backend, FillRule = rule.ToString(), Value = value });
        }
        return new { Passed = true, Checks = checks, Rows = rows };
    }
}
