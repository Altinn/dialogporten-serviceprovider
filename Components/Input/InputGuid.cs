using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Components;

public class InputGuid : InputBase<Guid?>
{
    public ElementReference? Element { get; protected set; }

    [Parameter] public bool Required { get; set; }

    [Parameter] public string ValidationMessage { get; set; } = "The value could not be parsed to a Guid.";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "type", "text");
        builder.AddAttribute(3, "class", CssClass);
        builder.AddAttribute(4, "value", BindConverter.FormatValue(CurrentValueAsString));
        builder.AddAttribute(5, "oninput", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.AddAttribute(6, "required", Required);
        builder.AddElementReferenceCapture(7, value => { Element = value; });
        builder.CloseElement();
    }

    protected override bool TryParseValueFromString(string? value, [NotNullWhen(true)] out Guid? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = Guid.Empty;
            validationErrorMessage = null;
            return true;
        }

        if (Guid.TryParse(value, out var guidValue))
        {
            result = guidValue;
            validationErrorMessage = null;
            return true;
        }
        result = Guid.Empty;
        validationErrorMessage = ValidationMessage;
        return false;
    }

    protected override string FormatValueAsString(Guid? value)
    {
        // Converts the Guid? to string, handling null appropriately
        return value.ToString() ?? string.Empty;
    }
}
