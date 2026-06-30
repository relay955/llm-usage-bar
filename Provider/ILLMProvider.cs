using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

public interface ILlmProvider {
    Task<Quota> GetCurrentQuotaAsync();
    Task<Balance> GetCurrentBalanceAsync(AppSettings settings);
    
    public abstract string Name { get; }
    public abstract bool HasShortQuota { get; }
    public abstract bool HasLongQuota { get; }
    public abstract bool HasBalance { get; }
    
    public abstract class Quota {
        public double Short { get; set; }
        public double Long { get; set; }
    }
    
    public abstract class Balance {
        public double Remain { get; set; }
        public double Max { get; set; }
    }
}

