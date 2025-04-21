using Newtonsoft.Json;
using TradingBot.Utilities.Extensions;

public class SecuritiesOverview
{
    public int Id { get; set; }
    // Primary Key
    public int SecurityID { get; set; }
    public string Symbol { get; set; }
    // Stock symbol, e.g., 'AAPL'
    public string Name { get; set; }
    // Company name
    public string Description { get; set; }
    // Company description
    public string CIK { get; set; }
    // Central Index Key (CIK)
    public string Exchange { get; set; }
    // Exchange, e.g., 'NASDAQ'
    public string Currency { get; set; }
    // Currency used, e.g., 'USD'
    public string Country { get; set; }
    // Country where the company is based
    public string Sector { get; set; }
    // Sector, e.g., 'TECHNOLOGY'
    public string Industry { get; set; }
    // Industry, e.g., 'ELECTRONIC COMPUTERS'
    public string Address { get; set; }
    // Address of the company
    public string OfficialSite { get; set; }
    // Official website URL
    public string FiscalYearEnd { get; set; }
    // Fiscal year end (e.g., 'September')
    public DateTime? LatestQuarter { get; set; }
    // The date of the most recent quarter
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? MarketCapitalization { get; set; }
    // Market capitalization in USD
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? EBITDA { get; set; }
    // Earnings Before Interest, Taxes, Depreciation, and Amortization
    [JsonProperty("PERatio")]
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? PERatio { get; set; }
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? PEGRatio { get; set; }
    // Price-to-Earnings Growth ratio
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? BookValue { get; set; }
    // Book value
    private string _dividendPerShare;
    public string DividendPerShare { get; set; }
    public double ParsedDividendPerShare { get; set; }
    private string _dividendYield;
    public string DividendYield { get; set; }
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double ParsedDividendYield { get; set; }
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? EPS { get; set; }
    // Earnings per share
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? RevenuePerShareTTM { get; set; }
    // Revenue per share (Trailing Twelve Months)
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? ProfitMargin { get; set; }
    // Profit margin
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? OperatingMarginTTM { get; set; }
    // Operating margin (Trailing Twelve Months)
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? ReturnOnAssetsTTM { get; set; }
    // Return on assets (Trailing Twelve Months)
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? ReturnOnEquityTTM { get; set; }
    // Return on equity (Trailing Twelve Months)
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? RevenueTTM { get; set; }
    // Revenue (Trailing Twelve Months)
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? GrossProfitTTM { get; set; }
    // Gross profit (Trailing Twelve Months)
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? DilutedEPSTTM { get; set; }
    // Diluted EPS (Trailing Twelve Months)
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? QuarterlyEarningsGrowthYOY { get; set; }
    // Quarterly earnings growth YoY
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? QuarterlyRevenueGrowthYOY { get; set; }
    // Quarterly revenue growth YoY
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? AnalystTargetPrice { get; set; }
    // Analyst target price
    public int AnalystRatingStrongBuy { get; set; }
    // Analyst rating count for Strong Buy
    public int AnalystRatingBuy { get; set; }
    // Analyst rating count for Buy
    public int AnalystRatingHold { get; set; }
    // Analyst rating count for Hold
    public int AnalystRatingSell { get; set; }
    // Analyst rating count for Sell
    public int AnalystRatingStrongSell { get; set; }
    // Analyst rating count for Strong Sell
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? TrailingPE { get; set; }
    // Trailing P/E ratio
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? ForwardPE { get; set; }
    // Forward P/E ratio
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? PriceToSalesRatioTTM { get; set; }
    // Price-to-sales ratio (Trailing Twelve Months)
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? PriceToBookRatio { get; set; }
    // Price-to-book ratio
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? EVToRevenue { get; set; }
    // EV/Revenue ratio
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? EVToEBITDA { get; set; }
    // EV/EBITDA ratio
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? Beta { get; set; }
    // Beta
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? FiftyTwoWeekHigh { get; set; }
    // 52-week high price
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? FiftyTwoWeekLow { get; set; }
    // 52-week low price
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? FiftyDayMovingAverage { get; set; }
    // 50-day moving average
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? TwoHundredDayMovingAverage { get; set; }
    // 200-day moving average
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? SharesOutstanding { get; set; }
    // Shares outstanding
    private string _dividendDate;
    public string DividendDate
    {
        get => _dividendDate;
        set
        {
            _dividendDate = value;
            ParsedDividendDate = ParseDate(value);
        }
    }
    public DateTime? ParsedDividendDate { get; private set; }

    private string _exDividendDate;
    public string ExDividendDate
    {
        get => _exDividendDate;
        set
        {
            _exDividendDate = value;
            ParsedExDividendDate = ParseDate(value);
        }
    }
    public DateTime? ParsedExDividendDate { get; private set; }

    private string _lastUpdated;
    public string LastUpdated
    {
        get => _lastUpdated;
        set
        {
            _lastUpdated = value;
            ParsedLastUpdated = ParseDate(value);
        }
    }
    public DateTime? ParsedLastUpdated { get; private set; }

    private DateTime? ParseDate(string date)
    {
        if (DateTime.TryParse(date, out DateTime parsedDate))
        {
            return parsedDate;
        }
        return null;
    }
}