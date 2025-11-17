using System;
using System.Globalization;
using System.Windows.Data;
using CSharp_WPF_Websockets.Domain.Entities;

namespace CSharp_WPF_Websockets.Presentation.Converters
{
    public class ValueDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 &&
                values[0] is double value &&
                values[1] is string type &&
                values[2] is string unit)
            {
                // Si hay una unidad definida, usarla directamente
                if (!string.IsNullOrEmpty(unit))
                {
                    // Casos especiales para valores digitales/booleanos
                    if (unit.ToLower() == "bool" ||
                        type?.ToLower() == "digital" ||
                        type?.ToLower() == "motion")
                    {
                        return value > 0 ? "ON" : "OFF";
                    }

                    // Para valores numéricos con unidad
                    return $"{value:F1} {unit}";
                }

                // Si no hay unidad, pero sí tipo, usar el tipo como referencia
                if (!string.IsNullOrEmpty(type))
                {
                    return type.ToLower() switch
                    {
                        "digital" or "motion" or "boolean" => value > 0 ? "ON" : "OFF",
                        "adc" => $"{value:F0}", // Sin decimales para ADC
                        _ => $"{value:F1}" // Valor con 1 decimal por defecto
                    };
                }

                // Fallback: solo el valor
                return $"{value:F1}";
            }

            return string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}