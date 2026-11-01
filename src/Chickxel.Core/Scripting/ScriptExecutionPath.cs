namespace Chickxel.Core.Scripting;

/// <summary>
/// 스크립트 효과 render가 도는 길(설계 8장). <see cref="Compiled"/>면 앱이 원문을 .NET 코드로 바꾼 빠른 경로로 돌고,
/// 아니면 Jint로 돈다. 그때 <see cref="Reason"/>은 처음 걸린 지원하지 않는 구문의 한 줄 설명이고 <see cref="Line"/>, <see cref="Column"/>은
/// 정리한 원문(<see cref="ScriptEffectProgram.Source"/>) 안의 위치(1부터, 모르면 0)다.
/// </summary>
public sealed record ScriptExecutionPath(bool Compiled, string? Reason, int Line, int Column)
{
    internal static ScriptExecutionPath Fast { get; } = new(true, null, 0, 0);
}
