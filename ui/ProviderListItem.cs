using System.ComponentModel;

namespace LLMUsageBar.ui;

public class ProviderListItem : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }
    public bool IsSelected { get; set; }

    /// <summary>
    /// 선택 목록에 표시할 프로바이더 이름을 초기화합니다.
    /// </summary>
    public ProviderListItem(string name) {
        Name = name;
    }
}
