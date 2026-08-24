using Extism.Sdk;

namespace Rune.Runtime.Wasm;

internal static class RuneHostFunctions
{
    public static HostFunction[] Create()
    {
        var reply =
            HostFunction.FromMethod<long>(
                "rune_message_reply",
                null!,
                static (
                    CurrentPlugin plugin,
                    long contentOffset) =>
                {
                    var context =
                        plugin.GetCallHostContext<
                            RuneExecutionContext>();

                    if (context is null)
                        return;

                    var content =
                        plugin.ReadString(
                            contentOffset);

                    context.Reply(content);
                });

        reply.SetNamespace(
            "extism:host/user");

        return [reply];
    }
}
