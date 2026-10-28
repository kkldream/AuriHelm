# 手動音訊延遲驗收

這是會改動真實音訊的驗收程式，不加入一般測試或建置腳本。每次執行前須取得使用者當次授權，不能以本文件或先前驗收推定未來操作許可。

核准後先保存當時設定、執行程序與音訊完整基準，正常退出舊版並啟動待驗收候選版，再另用全新目錄執行：

```powershell
./.tools/dotnet/dotnet.exe restore tests/AudioOutputManager.LatencyProbe --locked-mode
./.tools/dotnet/dotnet.exe build tests/AudioOutputManager.LatencyProbe -c Release --no-restore
./tests/AudioOutputManager.LatencyProbe/bin/Release/net10.0-windows10.0.26100.0/AudioOutputManager.LatencyProbe.exe --allow-audio-changes Realtek artifacts/acceptance/latency-全新日期時間
```

不帶明確開關時只印說明並退出，不初始化 Core Audio。輸出目錄已存在時拒絕覆寫。選擇名稱唯一符合的端點與其系統音效 session，不切換三角色、不更改設定、登入啟動或其他 app 音量。裝置未符合或缺少該 session 時停止，不猜測目標。

四條路徑各 30 次：端點音量／靜音、session 音量／靜音。音量在原值與相差 1% 的值間往返；靜音以原值與相反值往返。每次記錄寫入及自身讀回完成、另一個 Core Audio 實體直接讀回確認、通知抵達時間。背景以最高 20 Hz 採樣同一端點與 session 峰值，保存 CPU、峰值耗時及結構刷新次數。每筆立即寫入 samples.jsonl，summary.json 保存分布與失敗數；Windows 確認或通知 P95 超過 150 ms、讀回錯誤／缺通知回傳 1，取消為 3。

Ctrl+C 或在該目錄建立空白 `cancel` 檔會取消後續樣本，finally 還原本程式碰過的目標並直接讀回核對。不要用強制結束代替正常取消；裝置消失或原生 API 無法返回時仍可能無法還原，須保留當輪 baseline.json 並如實記錄，不能重放更早基準。基準包含裝置識別，只放本機忽略的 artifacts/acceptance/，不提交或上傳。

本工具不是 GUI 自動化，也不測 UI 渲染提交、實際聲音或耳朵端到端延遲。實際點擊／拖曳、雙視窗同步、焦點、白邊、動畫流暢度及隱藏後停採仍須另做原生驗收；不能以本工具通過取代畫面比對。
