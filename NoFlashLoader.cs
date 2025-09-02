// (新增内容B) 文件：NoFlashLoader.cs
using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FormBuffer // (按需改成你的命名空间)
{
    public static class NoFlashLoader
    {
        // (新增内容B) 反射访问受保护成员
        private static readonly MethodInfo SetStyleMethod =
            typeof(Control).GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo DoubleBufferedProp =
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);

        // =====================================================================
        // 方案一：标准附着（含 Opacity，支持淡入）。无透明控件时推荐。
        // 注意：Opacity 会让窗体成为 Layered Window，可能破坏“伪透明”控件背景。
        // =====================================================================
        public static void Attach(
            Form form,
            Func<Task> initAsync = null,     // (新增内容B) 后台初始化（IO/DB/设备/网络等）
            Action afterInitUI = null,       // (新增内容B) 初始化完成后的 UI 线程绑定/刷新
            bool fadeIn = true,              // (新增内容B) 是否淡入呈现
            int fadeDurationMs = 120,        // (新增内容B) 淡入时长（毫秒），0=瞬时
            bool applyAggressive = true,     // (新增内容B) 是否尽量开启样式优化（双缓冲等）
            bool enableRecursiveBuffer = true// (新增内容B) 是否递归对子控件开启双缓冲
        )
        {
            if (form == null) throw new ArgumentNullException("form");

            // (新增内容B) 启动透明，避免创建到首帧之间的“黑/白闪”
            TrySetOpacity(form, 0.0);

            if (applyAggressive)
            {
                TrySetStyle(form, ControlStyles.OptimizedDoubleBuffer, true);
                TrySetStyle(form, ControlStyles.AllPaintingInWmPaint, true);
                TrySetStyle(form, ControlStyles.ResizeRedraw, true);
                TrySetStyle(form, ControlStyles.UserPaint, true); // (新增内容B) 减少背景擦除（注意：可能影响透明）
                TrySetDoubleBuffered(form, true);

                if (enableRecursiveBuffer)
                    EnableDoubleBufferRecursive(form);
            }

            // (新增内容B) 兜底：有些场景外部可能提前 Show，这里再拉回透明
            form.HandleCreated += delegate { TrySetOpacity(form, 0.0); };

            bool initialized = false;
            form.Shown += async delegate
            {
                if (initialized) return;
                initialized = true;

                form.SuspendLayout();
                try
                {
                    if (initAsync != null)
                        await initAsync();     // (新增内容B) 后台初始化

                    if (afterInitUI != null)
                        afterInitUI();         // (新增内容B) UI 绑定/刷新
                }
                finally
                {
                    form.ResumeLayout(true);
                }

                // (新增内容B) 最终呈现：可淡入或瞬显
                if (fadeIn && fadeDurationMs > 0)
                    await FadeToAsync(form, 1.0, fadeDurationMs);
                else
                    TrySetOpacity(form, 1.0);
            };

            // (新增内容B) 用于一些第三方框架先触发 Load 的场景，保证仍然透明
            form.Load += delegate { TrySetOpacity(form, 0.0); };

            // (可选，新增内容B) 在透明阶段的 Paint，尽量用 BackColor 填充，弱化白底观感
            form.Paint += delegate (object sender, PaintEventArgs e)
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

        // ========================================================================================
        // 方案二：透明友好附着（不使用 Opacity，不设置 UserPaint），适合有 Transparent 背景控件。
        // 实现方式：启动时隐藏 → 后台初始化 → UI 绑定 → 一次性 Show()（无分层窗口）
        // ========================================================================================
        public static void AttachTransparentFriendly(
            Form form,
            Func<Task> initAsync = null,     // (新增内容B) 后台初始化（IO/DB/设备/网络等）
            Action afterInitUI = null,       // (新增内容B) 初始化完成后的 UI 线程绑定/刷新
            bool applyDoubleBuffer = true    // (新增内容B) 递归对子控件启用双缓冲（不影响透明链路）
        )
        {
            if (form == null) throw new ArgumentNullException("form");

            // (新增内容B) 不用 Opacity，直接隐藏，避免分层窗口破坏“伪透明”
            form.Visible = false;

            if (applyDoubleBuffer)
            {
                TrySetStyle(form, ControlStyles.OptimizedDoubleBuffer, true);
                TrySetStyle(form, ControlStyles.AllPaintingInWmPaint, true);
                TrySetStyle(form, ControlStyles.ResizeRedraw, true);
                // (注释内容B) 不设置 UserPaint，避免破坏透明父容器的代绘机制
                TrySetDoubleBuffered(form, true);
                EnableDoubleBufferRecursive(form);
            }

            bool initialized = false;
            form.Load += async delegate
            {
                if (initialized) return;
                initialized = true;

                form.SuspendLayout();
                try
                {
                    if (initAsync != null)
                        await initAsync();   // (新增内容B) 后台初始化

                    if (afterInitUI != null)
                        afterInitUI();       // (新增内容B) UI 绑定/刷新
                }
                finally
                {
                    form.ResumeLayout(true);
                }

                // (新增内容B) 一次性显示（非分层，无淡入，透明控件安全）
                form.Show();
            };
        }

        // ------------------------------ 公共小工具 ------------------------------

        private static void TrySetStyle(Control c, ControlStyles style, bool value)
        {
            try
            {
                if (SetStyleMethod != null)
                    SetStyleMethod.Invoke(c, new object[] { style, value });
            }
            catch { /* 忽略 */ }
        }

        private static void TrySetDoubleBuffered(Control c, bool value)
        {
            try
            {
                if (DoubleBufferedProp != null)
                    DoubleBufferedProp.SetValue(c, value, null);
            }
            catch { /* 忽略 */ }
        }

        private static void TrySetOpacity(Form f, double v)
        {
            try
            {
                if (v < 0.0) v = 0.0;
                if (v > 1.0) v = 1.0;
                f.Opacity = v;
            }
            catch { /* 某些嵌入/远程场景可能不支持透明，不致命 */ }
        }

        private static async Task FadeToAsync(Form form, double target, int durationMs)
        {
            if (target < 0.0) target = 0.0;
            if (target > 1.0) target = 1.0;

            if (durationMs <= 0)
            {
                TrySetOpacity(form, target);
                return;
            }

            const int steps = 12;
            int interval = durationMs / steps;
            if (interval < 8) interval = 8;

            double start = form.Opacity;
            int i;
            for (i = 1; i <= steps; i++)
            {
                double v = start + (target - start) * i / (double)steps;
                TrySetOpacity(form, v);
                await Task.Delay(interval);
            }
            TrySetOpacity(form, target);
        }

        /// <summary>
        /// (新增内容B) 递归开启所有子控件的双缓冲（对 DataGridView/Panel/TreeView 等效果明显）。
        /// </summary>
        public static void EnableDoubleBufferRecursive(Control root)
        {
            if (root == null) return;

            TrySetDoubleBuffered(root, true);
            TrySetStyle(root, ControlStyles.OptimizedDoubleBuffer, true);
            TrySetStyle(root, ControlStyles.AllPaintingInWmPaint, true);

            foreach (Control child in root.Controls)
                EnableDoubleBufferRecursive(child);
        }
    }
}
