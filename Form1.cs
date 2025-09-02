using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FormBuffer
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            // 直接附着
            NoFlashLoader.Attach(
                this,
                initAsync: async () =>
                {
                    // 这里写“耗时初始化”（放后台）
                    // 例如：读取配置/数据库/文件/设备握手等
                    await Task.Run(() => System.Threading.Thread.Sleep(200));
                },
                afterInitUI: () =>
                {
                    // 初始化完成后在 UI 线程做数据绑定/刷新
                    // uiDataGridView1.DataSource = data;
                },
                fadeIn: true,                // 需要“直接显示”就设成 false
                fadeDurationMs: 120,         // 淡入时长（毫秒）
                applyAggressive: true,       // 最大化样式优化（推荐）
                enableRecursiveBuffer: true  // 递归开启子控件双缓冲（推荐）
            );
        }

        private void button91_Click(object sender, EventArgs e)
        {
            Form2 form2 = new Form2();
            form2.ShowDialog();
        }
    }
}
