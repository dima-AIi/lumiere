using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Lumiere.Models;

/// <summary>HTML numeric inputs use a dot even when the server's locale uses a comma.</summary>
public sealed class DecimalPriceBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);
        if (decimal.TryParse(value.FirstValue?.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var price)) context.Result = ModelBindingResult.Success(price);
        else context.ModelState.TryAddModelError(context.ModelName, "Введите корректную цену.");
        return Task.CompletedTask;
    }
}
