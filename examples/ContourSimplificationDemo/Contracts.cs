namespace ContourSimplificationDemo;

internal sealed record CandidateRequest(double[][]? Points);
internal sealed record Preset(string Id, string Name, string Description, double[][] Points);
internal sealed record Candidate(int Index, double Epsilon, double[][] Points, int VertexCount,
    double ChangedArea, double UnionArea, double Error, double FirstArea, double SecondArea);
internal sealed record SkippedCandidate(int Index, double Epsilon, string Reason);
internal sealed record CandidateResponse(string FillRule, double[][] Original, double OriginalArea,
    Candidate[] Candidates, SkippedCandidate[] Skipped);

internal sealed class InputException(string message) : Exception(message);
