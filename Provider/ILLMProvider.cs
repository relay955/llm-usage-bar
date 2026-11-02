using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

public interface ILlmProvider {
    Task<Quota> GetCurrentQuotaAsync();
    Task<Balance> GetCurrentBalanceAsync(AppSettings settings);
    
    public abstract string Name { get; }
    public abstract string QuotaUrl { get; }
    public abstract bool HasShortQuota { get; }
    public abstract bool HasLongQuota { get; }
    public bool HasThirdQuota => false;
    public string ShortQuotaLabel => "";
    public string LongQuotaLabel => "";
    public string ThirdQuotaLabel => "";
    public string ThirdQuotaFormat => "{0:0.#}%";
    public abstract bool HasBalance { get; }
    
    public abstract class Quota {
        public double Short { get; set; }
        public double Long { get; set; }
        public double? Third { get; set; }
        public double? ThirdMax { get; set; }
        public DateTimeOffset? ShortResetAt { get; set; }
        public DateTimeOffset? LongResetAt { get; set; }
    }
    
    public abstract class Balance {
        public double Remain { get; set; }
        public double Max { get; set; }
    }
}
