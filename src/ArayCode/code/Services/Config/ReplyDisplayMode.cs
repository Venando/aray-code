namespace ArayCode;

public enum ReplyDisplayMode
{
    /// <summary>Use streaming delta events only (AgentReplyDeltaStart/Delta/End). Suppresses AgentReplyFull.</summary>
    Delta = 0,
    /// <summary>Use full reply events only (AgentReplyFull). Suppresses delta events.</summary>
    Full = 1,
    /// <summary>Both delta and full reply fire (default). Use this if unsure.</summary>
    Both = 2,
}
