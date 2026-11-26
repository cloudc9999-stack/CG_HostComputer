namespace CanHost.Models;

public static class Pt1000TemperatureConverter
{
    private const double ReferenceResistance = 1000.0;
    private const double A = 3.9083e-3;
    private const double B = -5.775e-7;
    private const double C = -4.183e-12;
    private const double TableMinimumTemperature = -200.0;
    private const double TableMaximumTemperature = 850.0;
    private const double TableStep = 0.1;

    private static readonly TemperaturePoint[] TemperatureTable = BuildTemperatureTable();

    public static bool TryConvertRaw(ushort rawValue, out double temperature)
    {
        temperature = double.NaN;
        if (rawValue >= 4095)
        {
            return false;
        }

        var denominator = 54600.0 - rawValue;
        if (denominator <= 0)
        {
            return false;
        }

        var resistance = 2000.0 * (rawValue + 27300.0) / denominator;
        return TryGetTemperature(resistance, out temperature);
    }

    public static double GetResistance(double temperature)
    {
        if (temperature >= 0)
        {
            return ReferenceResistance *
                   (1.0 + A * temperature + B * temperature * temperature);
        }

        return ReferenceResistance *
               (1.0 +
                A * temperature +
                B * temperature * temperature +
                C * (temperature - 100.0) * Math.Pow(temperature, 3));
    }

    public static bool TryGetTemperature(double resistance, out double temperature)
    {
        temperature = double.NaN;
        if (resistance < TemperatureTable[0].Resistance ||
            resistance > TemperatureTable[^1].Resistance)
        {
            return false;
        }

        var low = 0;
        var high = TemperatureTable.Length - 1;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (TemperatureTable[middle].Resistance <= resistance)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        var lower = TemperatureTable[low];
        var upper = TemperatureTable[high];
        var resistanceSpan = upper.Resistance - lower.Resistance;
        temperature = resistanceSpan <= double.Epsilon
            ? lower.Temperature
            : lower.Temperature +
              (resistance - lower.Resistance) /
              resistanceSpan *
              (upper.Temperature - lower.Temperature);
        return true;
    }

    private static TemperaturePoint[] BuildTemperatureTable()
    {
        var count = (int)Math.Round(
            (TableMaximumTemperature - TableMinimumTemperature) / TableStep) + 1;
        var table = new TemperaturePoint[count];
        for (var index = 0; index < count; index++)
        {
            var temperature = TableMinimumTemperature + index * TableStep;
            table[index] = new TemperaturePoint(
                temperature,
                GetResistance(temperature));
        }
        return table;
    }

    private readonly record struct TemperaturePoint(
        double Temperature,
        double Resistance);
}
