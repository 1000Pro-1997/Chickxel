using System.Globalization;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>AI 차례 끝 줄(멈춤·비용·걸린 시간)의 글. AI 채팅 창과 효과 만들기 AI 칸이 함께 쓴다.</summary>
internal static class ChatTurnFooter
{
    /// <summary>차례 끝 줄: 멈췄으면 멈춤 표시, 비용이 0보다 크면 "$0.000", 끝에 걸린 시간 — " · "로 잇는다.</summary>
    public static string Text(bool cancelled, double costUsd, double seconds)
    {
        var parts = new List<string>();
        if (cancelled) parts.Add(UiStrings.S_ChatWindow_EndTurn_D19A601695);
        if (costUsd > 0) parts.Add("$" + costUsd.ToString("0.000", CultureInfo.InvariantCulture));
        parts.Add(Seconds(seconds));
        return string.Join(" · ", parts);
    }

    /// <summary>걸린 시간 글(초, 소수 한 자리).</summary>
    public static string Seconds(double seconds)
        => seconds.ToString("0.0", CultureInfo.InvariantCulture) + UiStrings.S_ChatWindow_EndTurn_7EEAC40074;
}
