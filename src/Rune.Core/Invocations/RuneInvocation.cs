namespace Rune.Core.Invocations;

public abstract record RuneInvocation(
    ulong GuildId,
    ulong ChannelId,
    Guid InvocationId);
