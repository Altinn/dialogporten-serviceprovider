using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Components;

public class InputUri : InputBase<Uri?>
{
    public ElementReference? Element { get; protected set; }

    [Parameter] public string ValidationMessage { get; set; } = "Cant parse into URI";

    [Parameter] public bool Required { get; set; }

    protected override bool TryParseValueFromString(string? value, out Uri? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            validationErrorMessage = null;
            return true;
        }

        if (!Uri.IsWellFormedUriString(value, UriKind.RelativeOrAbsolute))
        {
            result = null;
            validationErrorMessage = ValidationMessage;
            return false;
        }

        if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out result))
        {
            validationErrorMessage = null;
            return true;
        }
        result = null;
        validationErrorMessage = ValidationMessage;
        return false;
    }

    protected override string FormatValueAsString(Uri? value)
    {
        return value?.ToString() ?? string.Empty;
    }

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
}
