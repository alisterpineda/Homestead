namespace Homestead.WebApi.Modules.Finances.Features.Reports.GetNetWorthHistory;

public enum NetWorthRange
{
    ThisMonth,
    ThreeMonths,
    SixMonths,
    TwelveMonths,
    YearToDate,
    LastYear,
    All,
    Custom,
}

public enum NetWorthGranularity
{
    Daily,
    Monthly,
}
