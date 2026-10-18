namespace BudgetApp.Services;

/// <summary>The one rule for storing money: amounts are kept to the kuruş, halves rounded away from zero.</summary>
public static class MoneyMath
{
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
