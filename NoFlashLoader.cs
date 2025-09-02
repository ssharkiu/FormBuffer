// (新增内容B) 文件：NoFlashLoader.cs
using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FormBuffer // 按需改成你的命名空间
{
    public static class NoFlashLoader
    {
        // (新增内容B) 反射句柄：访问受保护成员
        private static readonly MethodInfo SetStyleMethod =
            typeof(Control).GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo DoubleBufferedProp =
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// (新增内容B) 为任意 Form 启用“无闪屏加载”
        /// </summary>
        /// <param name="form">目标窗体（不需要继承自任何特殊基类）</param>
        /// <param name="initAsync">可选：耗时初始化逻辑（IO/数据库/设备/网络等）——放后台执行</param>
        /// <param name="afterInitUI">可选：初始化完成后在 UI 线程执行的 UI 绑定逻辑</param>
        /// <param name="fadeIn">是否淡入呈现</param>
        /// <param name="fadeDurationMs">淡入时长（毫秒），0 则瞬时显示</param>
        /// <param name="applyAggressive">是否尽量开启控件的双缓冲/用户绘制等样式</param>
        /// <param name="enableRecursiveBuffer">是否对所有子控件递归开启双缓冲</param>
        public static void Attach(
            Form form,
            Func<Task> initAsync = null,
            Action afterInitUI = null,
            bool fadeIn = true,
            int fadeDurationMs = 120,
            bool applyAggressive = true,
            bool enableRecursiveBuffer = true
        )
        {
            if (form == null) throw new ArgumentNullException(nameof(form));

            // (新增内容B) 尽早透明，避免创建到首帧之间的“黑/白闪”
            TrySetOpacity(form, 0.0);

            // (新增内容B) 可选：统一样式（不覆写基类，仅用反射调用 SetStyle/DoubleBuffered）
            if (applyAggressive)
            {
                TrySetStyle(form, ControlStyles.OptimizedDoubleBuffer, true);
                TrySetStyle(form, ControlStyles.AllPaintingInWmPaint, true);
                TrySetStyle(form, ControlStyles.ResizeRedraw, true);
                TrySetStyle(form, ControlStyles.UserPaint, true); // 尽量减少背景擦除
                TrySetDoubleBuffered(form, true);

                if (enableRecursiveBuffer)
                    EnableDoubleBufferRecursive(form);
            }

            // (新增内容B) 在 HandleCreated/Shown 时机再兜底透明一次，避免外部先显示造成的闪烁
            form.HandleCreated += (_, __) => { TrySetOpacity(form, 0.0); };

            bool initialized = false;
            form.Shown += async (_, __) =>
            {
                if (initialized) return;
                initialized = true;

                form.SuspendLayout();
                try
                {
                    // (新增内容B) 后台初始化：避免阻塞 UI 线程导致的背景重绘
                    if (initAsync != null)
                        await initAsync();

                    // (新增内容B) 初始化完成后在 UI 线程做数据绑定/控件刷新
                    afterInitUI?.Invoke();

                    // (新增内容B) 一次性呈现（可淡入）
                    if (fadeIn && fadeDurationMs > 0)
                        await FadeToAsync(form, 1.0, fadeDurationMs);
                    else
                        TrySetOpacity(form, 1.0);
                }
                finally
                {
                    form.ResumeLayout(performLayout: true);
                }
            };

            // (新增内容B) 兜底：在 Load 时也强制透明，防止第三方框架提前 Show()
            form.Load += (_, __) => { TrySetOpacity(form, 0.0); };

            // (可选，新增内容B) 使用 Paint 统一填充背景，弱化白底擦除的观感
            // 说明：我们不能覆写 OnPaintBackground，但可以在首帧前保持透明 + Paint 填充背景色
            form.Paint += (_, e) =>
            {
                if (form.Opacity < 1.0)
                {
                    using (var b = new SolidBrush(form.BackColor))
                    {
                        e.Graphics.FillRectangle(b, form.ClientRectangle);
                    }
                }
            };
        }

        // ---------------- 辅助函数：不改继承也能做到“尽可能防闪” ----------------

        private static void TrySetStyle(Control c, ControlStyles style, bool value)
        {
            try { SetStyleMethod?.Invoke(c, new object[] { style, value }); } catch { /* 忽略 */ }
        }

        private static void TrySetDoubleBuffered(Control c, bool value)
        {
            try { DoubleBufferedProp?.SetValue(c, value, null); } catch { /* 忽略 */ }
        }

        private static void TrySetOpacity(Form f, double v)
        {
            try
            {
                v = Math.Max(0.0, Math.Min(1.0, v));
                f.Opacity = v;
            }
            catch { /* 某些远程/嵌入场景不支持透明，不致命 */ }
        }

        private static async Task FadeToAsync(Form form, double target, int durationMs)
        {
            target = Math.Max(0.0, Math.Min(1.0, target));
            if (durationMs <= 0)
            {
                TrySetOpacity(form, target);
                return;
            }

            const int steps = 12;
            int interval = Math.Max(8, durationMs / steps);
            double start = form.Opacity;

            for (int i = 1; i <= steps; i++)
            {
                TrySetOpacity(form, start + (target - start) * i / steps);
                await Task.Delay(interval);
            }
            TrySetOpacity(form, target);
        }

        /// <summary>
        /// (新增内容B) 递归开启所有子控件的双缓冲（对 DataGridView/Panel/TreeView 等效果明显）
        /// </summary>
        public static void EnableDoubleBufferRecursive(Control root)
        {
            if (root == null) return;

            // 当前节点
            TrySetDoubleBuffered(root, true);
            TrySetStyle(root, ControlStyles.OptimizedDoubleBuffer, true);
            TrySetStyle(root, ControlStyles.AllPaintingInWmPaint, true);

            // 子节点
            foreach (Control child in root.Controls)
                EnableDoubleBufferRecursive(child);
        }
    }
}
