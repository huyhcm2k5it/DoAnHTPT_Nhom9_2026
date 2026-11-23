namespace De6.Transport.Tcp;

public enum FrameError
{
    InvalidLength,
    TruncatedHeader,
    TruncatedBody,
    InvalidUtf8,
    ChannelFaulted
}

/// <summary>The peer (or caller) broke the framing rules. The channel is closed after this is raised on a read.</summary>
public sealed class FrameProtocolException : IOException
{
    public FrameProtocolException(FrameError error, string message) : base(message) => Error = error;
    public FrameError Error { get; }
}

/// <summary>The peer closed the connection cleanly between two frames (EOF exactly on a frame boundary).</summary>
public sealed class FrameChannelClosedException : IOException
{
    public FrameChannelClosedException() : base("The peer closed the connection.") { }
}
