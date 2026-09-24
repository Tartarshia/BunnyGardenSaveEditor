[CmdletBinding()]
param([switch]$SelfTest)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$DefaultGameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\BUNNY GARDEN'
$Script:Flags = [Reflection.BindingFlags]'Instance,Public,NonPublic,DeclaredOnly'
$Script:Loaded = $null

function GameRoot([string]$path) {
  $marker='\Save\';$i=$path.IndexOf($marker,[StringComparison]::Ordinal)
  if($i -lt 1){throw 'Select a UserData file below the game Save directory.'};$path.Substring(0,$i)
}
function Types([string]$root) {
  $managed=Join-Path $root 'BUNNY GARDEN_Data\Managed'; $dll=Join-Path $managed 'Assembly-CSharp.dll'
  if(!(Test-Path -LiteralPath $dll)){throw "Game assembly not found: $dll"}
  if($Script:Loaded -ne $managed){[void][Reflection.Assembly]::LoadFrom($dll);$Script:Loaded=$managed}
}
function Saves([string]$root=$DefaultGameRoot) {
  $dir=Join-Path $root Save;if(!(Test-Path -LiteralPath $dir)){throw "Save directory not found: $dir"}
  $r=@(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter UserData|Where-Object{$_.FullName -match '\\user\\save\\'}|Sort-Object FullName)
  if($r.Count -eq 0){throw 'No UserData files found.'};$r
}
function Field([object]$o,[string]$name) {
  $t=$o.GetType();while($null -ne $t){$f=$t.GetField($name,$Script:Flags);if($f){return $f};$t=$t.BaseType};throw "Missing save field: $name"
}
function Val([object]$o,[string]$n){(Field $o $n).GetValue($o)}
function SetVal([object]$o,[string]$n,[object]$v){(Field $o $n).SetValue($o,$v)}
function ReadSave([string]$path) {
  Types (GameRoot $path);$file=$null;$zip=$null
  try{$file=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read);$zip=New-Object IO.Compression.DeflateStream($file,[IO.Compression.CompressionMode]::Decompress);(New-Object Runtime.Serialization.Formatters.Binary.BinaryFormatter).Deserialize($zip)}
  finally{if($zip){$zip.Dispose()};if($file){$file.Dispose()}}
}
function ReadBytes([byte[]]$bytes) {
  $ms=[IO.MemoryStream]::new($bytes);$zip=$null
  try{$zip=New-Object IO.Compression.DeflateStream($ms,[IO.Compression.CompressionMode]::Decompress);(New-Object Runtime.Serialization.Formatters.Binary.BinaryFormatter).Deserialize($zip)}
  finally{if($zip){$zip.Dispose()};$ms.Dispose()}
}
function Pack([object]$data) {
  $plain=New-Object IO.MemoryStream;$out=New-Object IO.MemoryStream;$zip=$null
  try{(New-Object Runtime.Serialization.Formatters.Binary.BinaryFormatter).Serialize($plain,$data);$plain.Position=0;$zip=New-Object IO.Compression.DeflateStream($out,[IO.Compression.CompressionMode]::Compress,$true);$plain.CopyTo($zip);$zip.Dispose();$zip=$null;$out.ToArray()}
  finally{if($zip){$zip.Dispose()};$out.Dispose();$plain.Dispose()}
}
function Slots([object]$data){@(Val $data m_savedGameData)}
function Snap([object]$slot,[int]$index){
  $c=@(Val $slot m_perCharacterDatas);[pscustomobject]@{Index=$index;Valid=[bool](Val $slot m_isValid);Date=[datetime](Val $slot m_saveDate);Money=[int](Val $slot m_money);Kana=[single](Val $c[0] '<Likability>k__BackingField');Rin=[single](Val $c[1] '<Likability>k__BackingField');Miuka=[single](Val $c[2] '<Likability>k__BackingField')}
}
function Apply([object]$data,[int]$index,[int]$money,[single]$kana,[single]$rin,[single]$miuka){
  $slot=(Slots $data)[$index];if(!(Val $slot m_isValid)){throw 'Cannot edit an empty slot.'};SetVal $slot m_money $money
  $reactive=Val $slot m_moneyForUI;$p=$reactive.GetType().GetProperty('Value',[Reflection.BindingFlags]'Instance,Public,NonPublic');if($p -and $p.CanWrite){$p.SetValue($reactive,$money,$null)}
  $c=@(Val $slot m_perCharacterDatas);$v=@($kana,$rin,$miuka);for($i=0;$i -lt 3;$i++){SetVal $c[$i] '<Likability>k__BackingField' ([single]$v[$i])}
}
function Verify([object]$s,[int]$money,[single]$kana,[single]$rin,[single]$miuka){if($s.Money -ne $money -or [math]::Abs($s.Kana-$kana)-gt .001 -or [math]::Abs($s.Rin-$rin)-gt .001 -or [math]::Abs($s.Miuka-$miuka)-gt .001){throw 'Read-back verification failed.'}}
function RoundTrip([string]$path){
  $a=ReadSave $path;$b=ReadBytes (Pack $a);$x=@(Slots $a);$y=@(Slots $b);for($i=0;$i -lt $x.Count;$i++){ $sx=Snap $x[$i] $i;$sy=Snap $y[$i] $i;if($sx.Valid -ne $sy.Valid -or $sx.Money -ne $sy.Money -or [math]::Abs($sx.Kana-$sy.Kana)-gt .001 -or [math]::Abs($sx.Rin-$sy.Rin)-gt .001 -or [math]::Abs($sx.Miuka-$sy.Miuka)-gt .001){throw "Round-trip failed for slot $i"}}
}
function AtomicWrite([string]$path,[byte[]]$bytes,[int]$index,[int]$money,[single]$kana,[single]$rin,[single]$miuka){
  $stamp=Get-Date -Format yyyyMMdd-HHmmss;$backup="$path.bak-$stamp";$tmp="$path.tmp-$PID"
  try{Copy-Item -LiteralPath $path -Destination $backup;[IO.File]::WriteAllBytes($tmp,$bytes);$check=ReadBytes ([IO.File]::ReadAllBytes($tmp));Verify (Snap (Slots $check)[$index] $index) $money $kana $rin $miuka;[IO.File]::Replace($tmp,$path,$null);$check=ReadSave $path;Verify (Snap (Slots $check)[$index] $index) $money $kana $rin $miuka;$backup}
  finally{if(Test-Path -LiteralPath $tmp){Remove-Item -LiteralPath $tmp -Force}}
}
function SlotNumber([string]$text){[int]([regex]::Match($text,'^Slot (\d+)').Groups[1].Value)}
function GameRunning(){$null -ne (Get-Process -Name 'BUNNY GARDEN' -ErrorAction SilentlyContinue)}

if($SelfTest){
  $path=(Saves)[0].FullName;RoundTrip $path;$data=ReadSave $path;$all=@(Slots $data);$index=-1
  for($i=0;$i -lt $all.Count;$i++){if((Snap $all[$i] $i).Valid){$index=$i;break}}
  if($index -lt 0){throw 'No non-empty slot available for the in-memory edit test.'}
  Apply $data $index 123456 1000 999 998;$check=ReadBytes (Pack $data);Verify (Snap (Slots $check)[$index] $index) 123456 1000 999 998
  Write-Output "PASS: in-memory round trip and four-field edit verification succeeded for $path. No save was written.";exit 0
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$form=New-Object Windows.Forms.Form;$form.Text='BUNNY GARDEN Save Editor';$form.Size=New-Object Drawing.Size(720,400);$form.StartPosition='CenterScreen';$form.FormBorderStyle='FixedDialog';$form.MaximizeBox=$false
$pathLabel=New-Object Windows.Forms.Label;$pathLabel.Location=New-Object Drawing.Point(18,15);$pathLabel.Size=New-Object Drawing.Size(660,37);$form.Controls.Add($pathLabel)
$slotsBox=New-Object Windows.Forms.ComboBox;$slotsBox.Location=New-Object Drawing.Point(18,53);$slotsBox.Size=New-Object Drawing.Size(514,28);$slotsBox.DropDownStyle='DropDownList';$form.Controls.Add($slotsBox)
$choose=New-Object Windows.Forms.Button;$choose.Text='Choose UserData...';$choose.Location=New-Object Drawing.Point(548,52);$choose.Size=New-Object Drawing.Size(130,27);$form.Controls.Add($choose)
$labels=@('Money (0 to 2000000000)','Kana affection (0 to 1000)','Rin affection (0 to 1000)','Miuka affection (0 to 1000)');$inputs=@();for($i=0;$i -lt 4;$i++){$l=New-Object Windows.Forms.Label;$l.Text=$labels[$i];$l.Location=New-Object Drawing.Point(18,(102+42*$i));$l.Size=New-Object Drawing.Size(245,25);$form.Controls.Add($l);$b=New-Object Windows.Forms.TextBox;$b.Location=New-Object Drawing.Point(270,(99+42*$i));$b.Size=New-Object Drawing.Size(180,26);$form.Controls.Add($b);$inputs+=$b}
$mirrors=New-Object Windows.Forms.CheckBox;$mirrors.Text='Also update exact duplicate UserData mirrors (recommended for Steam Auto-Cloud)';$mirrors.Location=New-Object Drawing.Point(18,276);$mirrors.Size=New-Object Drawing.Size(650,26);$mirrors.Checked=$true;$form.Controls.Add($mirrors)
$notice=New-Object Windows.Forms.Label;$notice.Text='Exit the game first. Each written file gets a timestamped backup and is read back after an atomic replacement.';$notice.Location=New-Object Drawing.Point(18,305);$notice.Size=New-Object Drawing.Size(660,35);$form.Controls.Add($notice)
$save=New-Object Windows.Forms.Button;$save.Text='Back up and save';$save.Location=New-Object Drawing.Point(510,338);$save.Size=New-Object Drawing.Size(168,30);$form.Controls.Add($save)
$Script:Path=$null;$Script:Data=$null
function LoadEditor([string]$path){$Script:Data=ReadSave $path;$Script:Path=$path;$pathLabel.Text=$path;$slotsBox.Items.Clear();$all=@(Slots $Script:Data);for($i=0;$i -lt $all.Count;$i++){$s=Snap $all[$i] $i;if($s.Valid){[void]$slotsBox.Items.Add(('Slot {0} | {1:yyyy-MM-dd HH:mm} | money {2:N0}' -f $i,$s.Date,$s.Money))}};if($slotsBox.Items.Count-eq 0){throw 'No non-empty slots.'};$slotsBox.SelectedIndex=0}
$slotsBox.add_SelectedIndexChanged({if($slotsBox.SelectedIndex-lt 0){return};$i=SlotNumber $slotsBox.SelectedItem;$s=Snap (Slots $Script:Data)[$i] $i;$inputs[0].Text=[string]$s.Money;$ci=[Globalization.CultureInfo]::InvariantCulture;$inputs[1].Text=$s.Kana.ToString($ci);$inputs[2].Text=$s.Rin.ToString($ci);$inputs[3].Text=$s.Miuka.ToString($ci)})
$choose.add_Click({$d=New-Object Windows.Forms.OpenFileDialog;$d.Title='Choose BUNNY GARDEN UserData';$d.Filter='UserData|UserData|All files|*.*';if($d.ShowDialog()-eq [Windows.Forms.DialogResult]::OK){try{LoadEditor $d.FileName}catch{[Windows.Forms.MessageBox]::Show($_.Exception.Message,'Could not read save')}}})
$save.add_Click({try{if(GameRunning){throw 'BUNNY GARDEN is running. Exit it first.'};$index=SlotNumber $slotsBox.SelectedItem;[int]$money=0;[single]$kana=0;[single]$rin=0;[single]$miuka=0;$ci=[Globalization.CultureInfo]::InvariantCulture;if(![int]::TryParse($inputs[0].Text,[ref]$money)-or $money-lt 0 -or $money-gt 2000000000){throw 'Invalid money.'};if(![single]::TryParse($inputs[1].Text,[Globalization.NumberStyles]::Float,$ci,[ref]$kana)-or $kana-lt 0 -or $kana-gt 1000){throw 'Invalid Kana affection.'};if(![single]::TryParse($inputs[2].Text,[Globalization.NumberStyles]::Float,$ci,[ref]$rin)-or $rin-lt 0 -or $rin-gt 1000){throw 'Invalid Rin affection.'};if(![single]::TryParse($inputs[3].Text,[Globalization.NumberStyles]::Float,$ci,[ref]$miuka)-or $miuka-lt 0 -or $miuka-gt 1000){throw 'Invalid Miuka affection.'};$ok=[Windows.Forms.MessageBox]::Show("Slot $index`nMoney: $money`nKana / Rin / Miuka: $kana / $rin / $miuka`n`nCreate backups and write?",'Confirm',[Windows.Forms.MessageBoxButtons]::OKCancel,[Windows.Forms.MessageBoxIcon]::Warning);if($ok-ne [Windows.Forms.DialogResult]::OK){return};$hash=(Get-FileHash -LiteralPath $Script:Path -Algorithm SHA256).Hash;$targets=@($Script:Path);if($mirrors.Checked){$targets=@(Saves (GameRoot $Script:Path)|Where-Object{(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash-eq $hash}|ForEach-Object FullName)};$backups=@();foreach($target in $targets){$data=ReadSave $target;Apply $data $index $money $kana $rin $miuka;$backups+=AtomicWrite $target (Pack $data) $index $money $kana $rin $miuka};LoadEditor $Script:Path;[Windows.Forms.MessageBox]::Show("Completed and verified.`nBackups:`n$($backups -join "`n")",'Done')}catch{[Windows.Forms.MessageBox]::Show($_.Exception.Message,'Save was not changed',[Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Error)}})
try{LoadEditor ((Saves)[0].FullName)}catch{$pathLabel.Text="No save automatically loaded: $($_.Exception.Message)"}
[void]$form.ShowDialog()
