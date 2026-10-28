# 參與 AuriHelm

歡迎提交問題、改善建議與 Pull Request。修改功能或互動前，先用 Issue 說明具體情境、預期行為與取捨，讓需求和驗收方式能在程式修改前確認。

## 建置與測試

需要 Windows 11 x64，以及 `global.json` 固定版本的 .NET SDK。相依套件由 `packages.lock.json` 鎖定；單純建置或測試不會安裝、啟動程式或變更音訊設定。

```powershell
git clone https://github.com/kkldream/AuriHelm.git
cd AuriHelm
./scripts/build.ps1
```

這個指令依序執行鎖定還原、Release 建置與產品測試。請保留鎖定檔；更新套件或 SDK 時，在 PR 說明必要性與相容性影響。

## 產生安裝包

另需官方 [NSIS 3.13](https://nsis.sourceforge.io/Download)。將 `makensis.exe` 加入 PATH，並保留 NSIS 安裝目錄的 `COPYING`。

```powershell
./scripts/package.ps1 -OutputDirectory artifacts/local-package
```

輸出資料夾必須尚未存在，且位於本專案 `artifacts/` 內。腳本會執行 NSIS 路徑與安裝交易回歸，再產生自含式 App 及未簽章 EXE 安裝包；不會執行真正安裝。產物包含本專案與第三方授權。

只需產生可執行目錄時：

```powershell
./scripts/build.ps1 -Publish -OutputDirectory artifacts/local-app
```

直接啟動 `artifacts/local-app/AudioOutputManager.App.exe` 時，須保留同目錄的全部相依檔案。

## 程式結構

| 位置 | 職責 |
|:--|:--|
| `src/AudioOutputManager.Core` | 輸出策略、穩定應用程式識別、設定與非 UI 狀態 |
| `src/AudioOutputManager.Windows` | Windows Core Audio、HID 與原生互通 |
| `src/AudioOutputManager.App` | WinUI 3 介面、系統匣與使用者操作 |
| `tests/` | 產品回歸與明確操作的驗證工具 |
| `scripts/`、`installer/` | 鎖定建置、授權收集與 NSIS 打包／回歸 |

內部組件與使用者資料路徑沿用 `AudioOutputManager` 識別，以維持既有設定相容。

## 驗證修改

- 先重現原問題，再補能區分修正前後行為的測試。文件與純文案修改以內容、連結及建置檢查為主。
- 有設計稿的畫面修改，提交前比對實作畫面與設計稿，包含展開／收合、焦點、明暗主題及需要的窄視窗狀態。
- 音量與靜音修改須分別驗證即時畫面、系統命令完成及實際讀回，不以畫面先變當成整個音訊操作完成。
- GUI、真實音訊、安裝、睡眠或裝置重連測試會改變使用者環境；先取得操作者同意，保存現況，測後還原。自動測試通過不等於這些實機項目通過。
- `tests/` 內的 probe 工具不屬於一般測試入口；先閱讀各自的 README，確認其是否會切換音訊、靜音或產生聲音。

## 提交 Pull Request

聚焦一個問題，說明觸發情境、修改後行為、驗證方式與尚未驗證的範圍。介面修改附實際截圖；不要附 AppData、原始診斷、裝置識別碼、個人路徑或測試還原資料。

提交內容須具有可授權的權利，並接受以本專案 MIT 授權散布。保留所有適用的第三方著作權與授權聲明，新增相依性時同步處理鎖定檔與發佈授權。

討論請尊重他人並聚焦可重現事實。安全問題請依 [SECURITY.md](SECURITY.md) 處理。
