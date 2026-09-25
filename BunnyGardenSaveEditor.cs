using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly ComboBox slots = new ComboBox(); private readonly TextBox[] input = new TextBox[4]; private readonly ComboBox dateBox = new ComboBox(); private readonly CheckBox dateCheck = new CheckBox(); private readonly CheckBox mirrors = new CheckBox(); private string path; private object data;
    public EditorForm()
    {
        Text = "兔兔秘密花园存档修改器"; Width = 720; Height = 465; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        var pathLabel = new Label { Left = 18, Top = 15, Width = 660, Height = 37 }; Controls.Add(pathLabel);
        slots.SetBounds(18, 53, 514, 28); slots.DropDownStyle = ComboBoxStyle.DropDownList; Controls.Add(slots);
        var choose = new Button { Text = "选择 UserData…", Left = 548, Top = 52, Width = 130 }; Controls.Add(choose);
        string[] labels = { "金钱（0 至 99999999）", "花奈好感度（0 至 1000）", "凛好感度（0 至 1000）", "美羽香好感度（0 至 1000）" };
        for (int i = 0; i < 4; i++) { Controls.Add(new Label { Text = labels[i], Left = 18, Top = 102 + 42 * i, Width = 245 }); input[i] = new TextBox { Left = 270, Top = 99 + 42 * i, Width = 180 }; Controls.Add(input[i]); }
        Controls.Add(new Label { Text = "游戏日期（仅可操作的周六、周日）", Left = 18, Top = 270, Width = 245 });
        dateBox.SetBounds(270, 267, 180, 26); dateBox.DropDownStyle = ComboBoxStyle.DropDownList; foreach (DateTime day in SaveCodec.PlayableDates()) dateBox.Items.Add(new GameDateChoice(day)); Controls.Add(dateBox);
        dateCheck.Text = "修改游戏日期"; dateCheck.SetBounds(470, 267, 180, 26); Controls.Add(dateCheck);
        dateCheck.CheckedChanged += delegate { dateBox.Enabled = dateCheck.Checked; };
        dateBox.Enabled = false;
        mirrors.Text = "同步写入内容完全相同的 UserData 镜像（推荐 Steam 自动云存档）"; mirrors.SetBounds(18, 318, 650, 26); mirrors.Checked = true; Controls.Add(mirrors);
        Controls.Add(new Label { Text = "请先退出游戏。跳转日期会同步前一天字段；每次写入均会备份并读回验证。", Left = 18, Top = 347, Width = 660, Height = 35 });
        var save = new Button { Text = "备份并保存修改", Left = 510, Top = 385, Width = 168 }; Controls.Add(save);
        slots.SelectedIndexChanged += delegate { if (slots.SelectedItem != null) Fill((Snapshot)slots.SelectedItem); };
        choose.Click += delegate { using (var dialog = new OpenFileDialog { Title = "选择 BUNNY GARDEN 的 UserData", Filter = "UserData|UserData|所有文件|*.*" }) if (dialog.ShowDialog() == DialogResult.OK) TryLoad(dialog.FileName, pathLabel); };
        save.Click += delegate { TrySave(pathLabel); };
        try { TryLoad(SaveCodec.FindSaves()[0], pathLabel); } catch (Exception ex) { pathLabel.Text = "未自动载入存档：" + ex.Message; }
    }
    private void TryLoad(string savePath, Label label) { try { data = SaveCodec.Read(savePath); path = savePath; label.Text = path; slots.Items.Clear(); Array all = SaveCodec.Slots(data); for (int i = 0; i < all.Length; i++) { Snapshot s = SaveCodec.Snap(all.GetValue(i), i); if (s.Valid) slots.Items.Add(s); } if (slots.Items.Count == 0) throw new InvalidOperationException("该文件没有非空存档槽位。"); slots.DisplayMember = "Index"; slots.SelectedIndex = 0; } catch (Exception ex) { MessageBox.Show(ex.Message, "无法读取存档", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
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
