using FluentValidation;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Tests.Unit.Common;

public class ValidationBehaviorTests
{
    private record TestRequest(string Name) : IRequest<string>;

    private class TestRequestValidator : AbstractValidator<TestRequest>
    {
        public TestRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
        }
    }

    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
            Enumerable.Empty<IValidator<TestRequest>>());

        var result = await behavior.Handle(
            new TestRequest("test"),
            () => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
            [new TestRequestValidator()]);

        var result = await behavior.Handle(
            new TestRequest("valid"),
            () => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task Handle_InvalidRequest_ThrowsValidationException()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
            [new TestRequestValidator()]);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(
                new TestRequest(""),
                () => Task.FromResult("ok"),
                CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == "Name");
    }
}
