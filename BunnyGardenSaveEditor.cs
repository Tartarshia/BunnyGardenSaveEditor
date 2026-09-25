using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") { SaveCodec.SelfTest(); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new EditorForm());
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "兔兔秘密花园存档修改器", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

internal sealed class Snapshot
{
    public int Index; public bool Valid; public DateTime SaveDate; public DateTime GameDate;
    public int Money; public float Kana; public float Rin; public float Miuka;
    public override string ToString() { return string.Format("槽位 {0:00}   保存于 {1:MM-dd HH:mm}   游戏日 {2:MM-dd}   金钱 {3:N0}", Index + 1, SaveDate, GameDate, Money); }
}

internal sealed class GameDateChoice
{
    public readonly DateTime Value;
    public GameDateChoice(DateTime value) { Value = value; }
    public override string ToString() { return Value.ToString("yyyy-MM-dd（dddd）", CultureInfo.GetCultureInfo("zh-CN")); }
}

internal static class SaveCodec
{
    public const string DefaultRoot = @"C:\Program Files (x86)\Steam\steamapps\common\BUNNY GARDEN";
    public const int MoneyMax = 99999999;
    private static string managedPath;
    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static string GameRoot(string path)
    {
        int index = path.IndexOf(@"\Save\", StringComparison.Ordinal);
        if (index < 1) throw new InvalidOperationException("请选择游戏 Save 目录下的 UserData 文件。");
        return path.Substring(0, index);
    }
    public static List<string> FindSaves(string root = DefaultRoot)
    {
        string saveRoot = Path.Combine(root, "Save");
        if (!Directory.Exists(saveRoot)) throw new DirectoryNotFoundException("找不到存档目录：" + saveRoot);
        var result = Directory.GetFiles(saveRoot, "UserData", SearchOption.AllDirectories)
            .Where(p => p.IndexOf(@"\user\save\", StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(p => p).ToList();
        if (result.Count == 0) throw new FileNotFoundException("没有找到 UserData 存档文件。");
        return result;
    }
    public static IEnumerable<DateTime> PlayableDates()
    {
        for (DateTime day = new DateTime(2023, 5, 6); day <= new DateTime(2023, 9, 24); day = day.AddDays(1))
            if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday) yield return day;
    }
    private static void LoadTypes(string root)
    {
        string path = Path.Combine(root, "BUNNY GARDEN_Data", "Managed");
        string assembly = Path.Combine(path, "Assembly-CSharp.dll");
        if (!File.Exists(assembly)) throw new FileNotFoundException("找不到游戏程序集：" + assembly);
        if (string.Equals(path, managedPath, StringComparison.OrdinalIgnoreCase)) return;
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string candidate = Path.Combine(path, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        Assembly.LoadFrom(assembly); managedPath = path;
    }
    private static FieldInfo Field(object value, string name)
    {
        for (Type type = value.GetType(); type != null; type = type.BaseType) {
            FieldInfo field = type.GetField(name, FieldFlags);
            if (field != null) return field;
        }
        throw new MissingFieldException("找不到所需存档字段：" + name);
    }
    private static object Get(object value, string name) { return Field(value, name).GetValue(value); }
    private static void Set(object value, string name, object replacement) { Field(value, name).SetValue(value, replacement); }
    public static object Read(string path)
    {
        LoadTypes(GameRoot(path));
        using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var zip = new DeflateStream(file, CompressionMode.Decompress)) return new BinaryFormatter().Deserialize(zip);
    }
    public static object Read(byte[] bytes)
    {
        using (var input = new MemoryStream(bytes))
        using (var zip = new DeflateStream(input, CompressionMode.Decompress)) return new BinaryFormatter().Deserialize(zip);
    }
    public static byte[] Pack(object data)
    {
        using (var plain = new MemoryStream()) {
            new BinaryFormatter().Serialize(plain, data); plain.Position = 0;
            using (var output = new MemoryStream()) {
                using (var zip = new DeflateStream(output, CompressionMode.Compress, true)) plain.CopyTo(zip);
                return output.ToArray();
            }
        }
    }
    public static Array Slots(object data) { return (Array)Get(data, "m_savedGameData"); }
    public static Snapshot Snap(object slot, int index)
    {
        Array c = (Array)Get(slot, "m_perCharacterDatas");
        return new Snapshot { Index = index, Valid = (bool)Get(slot, "m_isValid"), SaveDate = (DateTime)Get(slot, "m_saveDate"), GameDate = (DateTime)Get(slot, "m_gameDate"), Money = (int)Get(slot, "m_money"), Kana = (float)Get(c.GetValue(0), "<Likability>k__BackingField"), Rin = (float)Get(c.GetValue(1), "<Likability>k__BackingField"), Miuka = (float)Get(c.GetValue(2), "<Likability>k__BackingField") };
    }
    public static void Apply(object data, int index, int money, float kana, float rin, float miuka, DateTime? date)
    {
        object slot = Slots(data).GetValue(index);
        if (!(bool)Get(slot, "m_isValid")) throw new InvalidOperationException("不能修改空存档槽位。");
        Set(slot, "m_money", money);
        object reactive = Get(slot, "m_moneyForUI");
        PropertyInfo property = reactive.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite) property.SetValue(reactive, money, null);
        Array c = (Array)Get(slot, "m_perCharacterDatas"); float[] values = { kana, rin, miuka };
        for (int i = 0; i < 3; i++) Set(c.GetValue(i), "<Likability>k__BackingField", values[i]);
        if (date.HasValue) { Set(slot, "m_gameDate", date.Value.Date); Set(slot, "m_gamePreviousDate", date.Value.Date.AddDays(-1)); }
    }
    public static void Verify(Snapshot value, int money, float kana, float rin, float miuka, DateTime? date)
    {
        if (value.Money != money || Math.Abs(value.Kana - kana) > .001 || Math.Abs(value.Rin - rin) > .001 || Math.Abs(value.Miuka - miuka) > .001 || (date.HasValue && value.GameDate.Date != date.Value.Date)) throw new InvalidOperationException("写回验证失败：重新读取的数值与目标不一致。");
    }
    public static string AtomicWrite(string path, byte[] bytes, int index, int money, float kana, float rin, float miuka, DateTime? date)
    {
        string backup = path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); string temp = path + ".tmp-" + Process.GetCurrentProcess().Id;
        try {
            File.Copy(path, backup, false); File.WriteAllBytes(temp, bytes);
            Verify(Snap(Slots(Read(File.ReadAllBytes(temp))).GetValue(index), index), money, kana, rin, miuka, date);
            File.Replace(temp, path, null);
            Verify(Snap(Slots(Read(path)).GetValue(index), index), money, kana, rin, miuka, date);
            return backup;
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string Sha256(string path) { using (var hash = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)); }
    public static bool GameRunning() { return Process.GetProcessesByName("BUNNY GARDEN").Length != 0; }
    public static void SelfTest()
    {
        string path = FindSaves()[0]; object data = Read(path); Array slots = Slots(data); int index = -1;
        for (int i = 0; i < slots.Length; i++) if (Snap(slots.GetValue(i), i).Valid) { index = i; break; }
        if (index < 0) throw new InvalidOperationException("没有可用于内存测试的非空槽位。");
        DateTime date = new DateTime(2023, 8, 13); Apply(data, index, 123456, 1000, 999, 998, date);
        Verify(Snap(Slots(Read(Pack(data))).GetValue(index), index), 123456, 1000, 999, 998, date);
        Console.WriteLine("PASS: 未写入存档的内存读写验证通过。");
    }
}

internal sealed class EditorForm : Form
{
    private static readonly Color Ink = Color.FromArgb(58, 42, 62);
    private static readonly Color Pink = Color.FromArgb(226, 82, 139);
    private static readonly Color PalePink = Color.FromArgb(255, 244, 249);
    private static readonly Color Card = Color.FromArgb(255, 255, 255);
    private readonly ComboBox slots = new ComboBox(); private readonly TextBox[] input = new TextBox[4]; private readonly ComboBox dateBox = new ComboBox(); private readonly CheckBox dateCheck = new CheckBox(); private readonly CheckBox mirrors = new CheckBox(); private string path; private object data;
    private void AddCard(int left, int top, int width, int height)
    {
        var card = new Panel { Left = left, Top = top, Width = width, Height = height, BackColor = Card, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(card); card.SendToBack();
    }
    private static void StyleInput(Control control)
    {
        control.Font = new Font("Segoe UI", 10F); control.ForeColor = Ink; control.BackColor = Color.White;
    }
    private static Label MakeLabel(string text, int left, int top, int width)
    {
        return new Label { Text = text, Left = left, Top = top, Width = width, Height = 24, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Ink, BackColor = Color.Transparent };
    }
    public EditorForm()
    {
        Text = "兔兔秘密花园存档修改器"; Width = 760; Height = 550; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; BackColor = PalePink; Font = new Font("Segoe UI", 9F);
        var header = new Panel { Left = 0, Top = 0, Width = 760, Height = 96, BackColor = Pink }; Controls.Add(header); header.SendToBack();
        Controls.Add(new Label { Text = "BUNNY GARDEN", Left = 20, Top = 16, Width = 300, Height = 30, Font = new Font("Segoe UI", 18F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent });
        Controls.Add(new Label { Text = "本地存档修改器  ·  自动备份与读回验证", Left = 22, Top = 49, Width = 430, Height = 24, Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(255, 232, 241), BackColor = Color.Transparent });
        var pathLabel = new Label { Left = 22, Top = 72, Width = 710, Height = 20, Font = new Font("Segoe UI", 8F), ForeColor = Color.FromArgb(255, 232, 241), BackColor = Color.Transparent, AutoEllipsis = true }; Controls.Add(pathLabel);
        AddCard(18, 112, 724, 73); AddCard(18, 197, 724, 151); AddCard(18, 360, 724, 64); AddCard(18, 436, 724, 72);
        Controls.Add(MakeLabel("选择存档槽位", 34, 122, 180));
        slots.SetBounds(34, 146, 526, 28); slots.DropDownStyle = ComboBoxStyle.DropDownList; StyleInput(slots); Controls.Add(slots);
        var choose = new Button { Text = "选择 UserData…", Left = 574, Top = 145, Width = 150, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(247, 222, 235), ForeColor = Ink }; choose.FlatAppearance.BorderColor = Pink; Controls.Add(choose);
        string[] labels = { "金钱（0 至 99999999）", "花奈好感度（0 至 1000）", "凛好感度（0 至 1000）", "美羽香好感度（0 至 1000）" };
        Controls.Add(MakeLabel("修改数值", 34, 207, 180));
        for (int i = 0; i < 4; i++) { int column = i % 2; int row = i / 2; Controls.Add(MakeLabel(labels[i], 34 + column * 347, 239 + row * 48, 250)); input[i] = new TextBox { Left = 34 + column * 347, Top = 263 + row * 48, Width = 306, BorderStyle = BorderStyle.FixedSingle }; StyleInput(input[i]); Controls.Add(input[i]); }
        Controls.Add(MakeLabel("游戏日期", 34, 371, 120));
        Controls.Add(new Label { Text = "仅列出可操作的周六、周日", Left = 115, Top = 373, Width = 200, Height = 22, ForeColor = Color.FromArgb(134, 106, 125), Font = new Font("Segoe UI", 8.5F), BackColor = Color.Transparent });
        dateBox.SetBounds(34, 394, 306, 26); dateBox.DropDownStyle = ComboBoxStyle.DropDownList; StyleInput(dateBox); foreach (DateTime day in SaveCodec.PlayableDates()) dateBox.Items.Add(new GameDateChoice(day)); Controls.Add(dateBox);
        dateCheck.Text = "启用日期跳转"; dateCheck.SetBounds(365, 392, 160, 26); dateCheck.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold); dateCheck.ForeColor = Ink; dateCheck.BackColor = Color.Transparent; Controls.Add(dateCheck);
        dateCheck.CheckedChanged += delegate { dateBox.Enabled = dateCheck.Checked; };
        dateBox.Enabled = false;
        mirrors.Text = "同步内容完全相同的 UserData 镜像（推荐 Steam 自动云存档）"; mirrors.SetBounds(34, 448, 455, 26); mirrors.Checked = true; mirrors.Font = new Font("Segoe UI", 9F); mirrors.ForeColor = Ink; mirrors.BackColor = Color.Transparent; Controls.Add(mirrors);
        Controls.Add(new Label { Text = "先退出游戏；保存会创建备份、原子替换并读回验证。", Left = 34, Top = 476, Width = 455, Height = 22, ForeColor = Color.FromArgb(134, 106, 125), Font = new Font("Segoe UI", 8.5F), BackColor = Color.Transparent });
        var save = new Button { Text = "备份并保存修改", Left = 523, Top = 455, Width = 201, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Pink, ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold) }; save.FlatAppearance.BorderSize = 0; Controls.Add(save);
        slots.SelectedIndexChanged += delegate { if (slots.SelectedItem != null) Fill((Snapshot)slots.SelectedItem); };
        choose.Click += delegate { using (var dialog = new OpenFileDialog { Title = "选择 BUNNY GARDEN 的 UserData", Filter = "UserData|UserData|所有文件|*.*" }) if (dialog.ShowDialog() == DialogResult.OK) TryLoad(dialog.FileName, pathLabel); };
        save.Click += delegate { TrySave(pathLabel); };
        try { TryLoad(SaveCodec.FindSaves()[0], pathLabel); } catch (Exception ex) { pathLabel.Text = "未自动载入存档：" + ex.Message; }
    }
    private void TryLoad(string savePath, Label label) { try { data = SaveCodec.Read(savePath); path = savePath; label.Text = path; slots.Items.Clear(); Array all = SaveCodec.Slots(data); for (int i = 0; i < all.Length; i++) { Snapshot s = SaveCodec.Snap(all.GetValue(i), i); if (s.Valid) slots.Items.Add(s); } if (slots.Items.Count == 0) throw new InvalidOperationException("该文件没有非空存档槽位。"); slots.SelectedIndex = 0; } catch (Exception ex) { MessageBox.Show(ex.Message, "无法读取存档", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    private void Fill(Snapshot s) { input[0].Text = s.Money.ToString(); input[1].Text = s.Kana.ToString(CultureInfo.InvariantCulture); input[2].Text = s.Rin.ToString(CultureInfo.InvariantCulture); input[3].Text = s.Miuka.ToString(CultureInfo.InvariantCulture); for (int i = 0; i < dateBox.Items.Count; i++) if (((GameDateChoice)dateBox.Items[i]).Value.Date == s.GameDate.Date) { dateBox.SelectedIndex = i; break; } }
    private void TrySave(Label label)
    {
        try {
            if (SaveCodec.GameRunning()) throw new InvalidOperationException("检测到游戏正在运行，请先退出游戏。"); if (slots.SelectedItem == null) throw new InvalidOperationException("请先选择存档槽位。");
            int money; float kana, rin, miuka; if (!int.TryParse(input[0].Text, out money) || money < 0 || money > SaveCodec.MoneyMax) throw new InvalidOperationException("金钱必须是 0 至 99999999 的整数。超过 1 亿会导致游戏内显示错误。");
            if (!float.TryParse(input[1].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out kana) || kana < 0 || kana > 1000) throw new InvalidOperationException("花奈好感度必须是 0 至 1000 的数字。");
            if (!float.TryParse(input[2].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out rin) || rin < 0 || rin > 1000) throw new InvalidOperationException("凛好感度必须是 0 至 1000 的数字。");
            if (!float.TryParse(input[3].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out miuka) || miuka < 0 || miuka > 1000) throw new InvalidOperationException("美羽香好感度必须是 0 至 1000 的数字。");
            DateTime? date = null; if (dateCheck.Checked) { if (dateBox.SelectedItem == null) throw new InvalidOperationException("请选择一个可操作的周六或周日。"); date = ((GameDateChoice)dateBox.SelectedItem).Value; }
            Snapshot selected = (Snapshot)slots.SelectedItem; string summary = string.Format("槽位 {0}\n金钱：{1}\n花奈 / 凛 / 美羽香：{2} / {3} / {4}\n游戏日期：{5}\n\n是否创建备份并写入？", selected.Index, money, kana, rin, miuka, date.HasValue ? date.Value.ToString("yyyy-MM-dd") : "不变");
            if (MessageBox.Show(summary, "确认修改", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            string hash = SaveCodec.Sha256(path); var targets = mirrors.Checked ? SaveCodec.FindSaves(SaveCodec.GameRoot(path)).Where(p => SaveCodec.Sha256(p) == hash).ToList() : new List<string> { path }; var backups = new List<string>();
            foreach (string target in targets) { object targetData = SaveCodec.Read(target); SaveCodec.Apply(targetData, selected.Index, money, kana, rin, miuka, date); backups.Add(SaveCodec.AtomicWrite(target, SaveCodec.Pack(targetData), selected.Index, money, kana, rin, miuka, date)); }
            TryLoad(path, label); MessageBox.Show("修改完成并已读回验证。\n备份：\n" + string.Join("\n", backups), "完成");
        } catch (Exception ex) { MessageBox.Show(ex.Message, "未写入存档", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
