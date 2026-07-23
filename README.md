# PublishManager

一個 **Avalonia 12 + .NET 10** 的 Windows 桌面 app,用來統一管理多個 GitHub 專案的發版(release)流程:加入專案 → 用 git tag 管理版號 → 一鍵發版(推 tag 或觸發 workflow_dispatch)→ 在 app 內即時監看觸發到的 GitHub Actions run。

## 建置與執行

```bash
dotnet build PublishManager.slnx -c Debug
```

```bash
dotnet run --project src/PublishManager/PublishManager.csproj
```

執行測試:

```bash
dotnet test PublishManager.slnx
```

## 專案結構

- `src/PublishManager/` — Avalonia UI(MVVM,CommunityToolkit.Mvvm,Generic-Host DI)
- `src/PublishManager.Core/` — 領域邏輯與服務(不參考 Avalonia,可單元測試)
- `tests/PublishManager.Tests/` — xUnit 測試

## 核心概念

### 版號 = Git tag
版號的真實來源是 **git tag**(SemVer)。app 讀取既有 tag、依所選遞增類型(major / minor / patch / prerelease)推算下一版。可設定 tag 前綴(預設 `v`)。

### 兩種發版模型(每專案可選)
1. **TagPush** — 本機依序執行你設定的步驟(建置/打包等)→ 建立並推送 annotated tag → 由 tag 觸發 repo 的 GitHub Action。
2. **WorkflowDispatch** — 透過 GitHub API 直接觸發雲端 workflow(帶 inputs)。

發版流程:preflight(乾淨工作目錄 / 分支 / 遠端 tag 不存在)→ 算版號 → 本機步驟 → 觸發 → 定位並監看 run。支援 **dry-run**(只計算、不推送/不觸發),建議先跑一次 dry-run。

### 傳給步驟的環境變數
每個本機步驟執行時都會注入:
- `RELEASE_VERSION` — 不含前綴的版號(例:`1.2.3`)
- `RELEASE_TAG` — 含前綴的 tag(例:`v1.2.3`)

例如 PowerShell 步驟可用 `dotnet build -p:Version=$env:RELEASE_VERSION`。

### Dispatch inputs 的變數替換
WorkflowDispatch 模型下,dispatch inputs 的**值**若包含 `$VERSION` 或 `$TAG`,會分別替換成計算出的版號與 tag。例如把 input `version` 設為 `$VERSION`。(inputs 名稱需與 workflow yaml 宣告的一致。)

## GitHub 認證
app 重用已登入的 `gh` CLI(`gh auth token`),token 只存記憶體、不落地。需要 `repo` 與 `workflow` scope。若未安裝/未登入 gh,可改用 PAT。

> 注意:`git push`(TagPush 模型)使用的是 git 自己的認證(Git Credential Manager),與 gh token 是兩回事。

## GitHub Actions 監看
觸發後 app 內即時輪詢 run / job / step 狀態並顯示時間軸。**log 於每個 job 完成時**取得(GitHub REST API 無法在 job 執行中即時串流 log)。本機步驟的輸出則是真正即時逐行串流。

## 資料儲存
專案設定存於 `%APPDATA%\PublishManager\projects.json`(原子寫入 + schemaVersion)。
