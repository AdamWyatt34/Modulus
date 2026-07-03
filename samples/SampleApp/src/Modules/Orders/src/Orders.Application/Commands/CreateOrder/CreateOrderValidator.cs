using FluentValidation;

namespace SampleApp.Orders.Application.Commands.CreateOrder;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderValidator()
    {
        RuleFor(c => c.CustomerName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Total).GreaterThan(0);
    }
}
