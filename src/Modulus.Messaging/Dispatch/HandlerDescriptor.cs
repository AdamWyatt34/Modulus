namespace Modulus.Messaging.Dispatch;

/// <summary>
/// A resolved handler for one event instance: its name (the inbox idempotency key
/// component) and an untyped invoker bound to the handler instance.
/// </summary>
internal sealed record HandlerDescriptor(string Name, Func<object, CancellationToken, Task> Handle);
