namespace KnowledgeHub.Server.Agent;

/// <summary>Loop bounds for the agentic tool-calling loop (SPEC-20260914-agent-chat-loop RF-001/RNF).</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Hard cap on model→tools→model rounds.</summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>Hard cap on total tool invocations across all iterations.</summary>
    public int MaxToolCalls { get; set; } = 20;

    /// <summary>Tool results are truncated to this many chars before going back to the model.</summary>
    public int MaxToolResultChars { get; set; } = 4000;
}
