namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents the next action in the pipeline, returning a <typeparamref name="TResponse"/>.
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="cancellationToken"/> lets a pipeline behavior substitute its own token for
/// everything further down the pipeline — inner behaviors and, ultimately, the handler itself.
/// A behavior that wants a timeout, or any other linked-token pattern, creates a
/// <see cref="CancellationTokenSource"/> and calls <c>next(linkedCts.Token)</c>: every step
/// between that call and the handler receives the substituted token instead of the one the
/// outer behavior was given.
/// </para>
/// <para>
/// <b>Convention:</b> calling <c>next()</c> with no argument passes the parameter's default
/// value, which the mediator's pipeline wiring treats as <i>"keep the token I was given"</i> —
/// not as an explicit request for <see cref="CancellationToken.None"/>. This is what keeps
/// pre-4.0 <c>await next()</c> call sites source-compatible: a behavior that never touches
/// cancellation still flows whatever token it received, unchanged, to the next step.
/// </para>
/// <para>
/// <b>Caveat:</b> because of that convention, a behavior that explicitly passes
/// <see langword="default"/> (or an uninitialized <see cref="CancellationToken"/>) is
/// indistinguishable from one that omits the argument entirely — both flow the token the
/// current behavior received rather than truly resetting to <see cref="CancellationToken.None"/>.
/// A behavior that needs to guarantee no cancellation reaches downstream code must not rely on
/// this delegate for that; it should thread its own non-cancelable token through the handler by
/// another means.
/// </para>
/// </remarks>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
/// <param name="cancellationToken">
/// The token to flow to the next step in the pipeline. Defaults to <see langword="default"/>,
/// which is treated as "use the token the current behavior received" — see the convention and
/// caveat above.
/// </param>
/// <returns>A task producing the response.</returns>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);
