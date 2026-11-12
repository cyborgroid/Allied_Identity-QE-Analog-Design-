using Avalonia.Controls;

namespace AlliedID.Shell.Widgets;

/// <summary>화면에 붙어 있는 동안에만 이벤트를 구독 (뷰 재조립 시 누수 방지).</summary>
public static class Hooks
{
    public static T WhileAttached<T>(this T control, Action subscribe, Action unsubscribe) where T : Control
    {
        control.AttachedToVisualTree += (_, _) => subscribe();
        control.DetachedFromVisualTree += (_, _) => unsubscribe();
        return control;
    }

    public static T OnTick<T>(this T control, ShellContext ctx, Action a) where T : Control =>
        control.WhileAttached(() => { ctx.Tick += a; a(); }, () => ctx.Tick -= a);

    public static T OnStatus<T>(this T control, ShellContext ctx, Action a) where T : Control =>
        control.WhileAttached(() => { ctx.StatusChanged += a; a(); }, () => ctx.StatusChanged -= a);
}
