using System.Globalization;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>
/// AI 모델 칸(콤보 상자)과 그 목록: 지금 AI의 모델 목록(<see cref="AiProvider.Models"/>)으로 칸을 채우고 설정에 남은 모델을 고르며,
/// 사용자가 모델을 바꾸면 설정에 바로 남긴 뒤 창에 알린다(세션 다시 띄우기·차례 멈춤은 창 몫).
/// AI 채팅 창과 효과 만들기 AI 칸이 같은 설정 값(<see cref="AiProvider.GetModel"/>)을 읽고 쓴다.
/// </summary>
internal sealed class ChatModelSelector
{
    private readonly ComboBox _box;
    private readonly AppSettings _settings;
    private readonly Func<AiProvider> _provider;
    private readonly Action<string?, string> _changed;
    /// <summary>목록을 채우거나 코드로 고르는 중(칸의 바뀜을 사용자가 고른 것으로 보지 않음).</summary>
    private bool _filling;

    /// <param name="box">모델 칸(모양·자리는 창이 정함).</param>
    /// <param name="settings">모델을 읽고 쓰는 설정.</param>
    /// <param name="provider">지금 AI(창에서 AI를 바꾸면 바뀐 AI).</param>
    /// <param name="changed">사용자가 모델을 바꿔 설정에 남긴 뒤 부른다: 새 모델 값(null = CLI 기본값), 대화에 남길 안내 줄.</param>
    public ChatModelSelector(ComboBox box, AppSettings settings, Func<AiProvider> provider, Action<string?, string> changed)
    {
        _box = box;
        _settings = settings;
        _provider = provider;
        _changed = changed;
        box.SelectionChanged += (_, _) => OnChanged();
        Fill();
    }

    /// <summary>모델 목록(<see cref="ComboBox"/>의 항목 순서).</summary>
    public IReadOnlyList<ModelChoice> Choices { get; private set; } = [];

    /// <summary>고른 모델 값(목록이 비었으면 null).</summary>
    public string? Current
        => Choices.Count == 0 ? null : Choices[Math.Clamp(_box.SelectedIndex, 0, Choices.Count - 1)].Value;

    /// <summary>모델 칸을 지금 AI의 목록으로 채우고 설정에 남은 모델을 고른다(이름에 실제 버전이 붙으면 다시 부름).</summary>
    public void Fill()
    {
        var provider = _provider();
        _filling = true;
        try
        {
            Choices = provider.Models(_settings);
            _box.Items.Clear();
            foreach (var c in Choices)
            {
                var item = new ComboBoxItem { Content = c.Label };
                if (c.Tip is { Length: > 0 } tip) item.Tip(tip);
                _box.Items.Add(item);
            }
            var saved = provider.GetModel(_settings);
            _box.SelectedIndex = Math.Max(0, Choices.ToList().FindIndex(c => c.Value == saved));
            _box.Tip(string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_FillModels_B806D71CEE, provider.Name, provider.ProductName));
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>
    /// 모델 칸을 <paramref name="model"/>로 고르고 설정에도 저장한다(창에 알리지 않음). 목록에 없는 모델(예전 별칭·직접 넣은 이름)이면
    /// 설정에 넣고 목록을 다시 채운다(AI마다 설정에 저장된 모델은 목록에 더해 보임).
    /// </summary>
    public void Select(string? model)
    {
        if (Current == model) return;
        _provider().SetModel(_settings, model);
        int index = Choices.ToList().FindIndex(c => c.Value == model);
        if (index < 0)
        {
            Fill();
            return;
        }
        _filling = true;
        try
        {
            _box.SelectedIndex = index;
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>사용자가 모델을 바꿈: 설정에 바로 남기고 창에 알린다(같은 모델이면 아무것도 하지 않음).</summary>
    private void OnChanged()
    {
        if (_filling || _box.SelectedIndex < 0) return;
        var model = Current;
        var provider = _provider();
        if (provider.GetModel(_settings) == model) return;
        provider.SetModel(_settings, model);
        _settings.Save();
        _changed(model, string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_OnModelChanged_C640BCC05A, Choices[_box.SelectedIndex].Label));
    }
}
