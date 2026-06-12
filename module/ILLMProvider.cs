namespace LLMUsageBar.module;

public interface ILlmProvider {
    Task<Quota> GetCurrentQuotaAsync();
    Task<Balance> GetCurrentBalanceAsync();
    
    public abstract class Quota {
        public double Daily { get; set; }
        public double Weekly { get; set; }
    }
    
    public abstract class Balance {
        public double Remain { get; set; }
    }
}


