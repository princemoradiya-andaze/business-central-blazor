using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace BusinessCentralBlazor.Web.Components;

// Keep the draft current while typing; the card persists it on change/focusout, not on every keystroke.
public sealed class CardInputText : InputText
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "class", CssClass);
        builder.AddAttribute(3, "value", CurrentValueAsString);
        builder.AddAttribute(4, "oninput", EventCallback.Factory.CreateBinder<string?>(this,
            value => CurrentValueAsString = value, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        builder.AddElementReferenceCapture(5, reference => Element = reference);
        builder.CloseElement();
    }
}
