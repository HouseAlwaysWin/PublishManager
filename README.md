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

### 兩種觸發方式(每專案可選)
1. **TagPush** — 本機依序執行你設定的步驟(建置/打包等)→ 建立並推送 annotated tag → 由 tag 觸發 repo 的 GitHub Action。
2. **WorkflowDispatch** — 透過 GitHub API 直接觸發雲端 workflow(帶 inputs)。

發版流程:preflight(乾淨工作目錄 / 分支 / 遠端 tag 不存在)→ 算版號 → 本機步驟 → 觸發 → 定位並監看 run。

一次發版只會監看**一個** workflow run;若偵測到 repo 裡還有其他吃 tag push 的 workflow,新增/編輯專案時會明確警告。

### 發版來源(從哪個 branch / commit 發版)
發版面板的「**發版來源**」可填**分支、tag 或 commit sha**;留空就是目前 checkout 的東西。輸入框會提示本機與遠端分支。

- **不會動你的工作目錄** —— tag 直接打在指定的 commit 上(`git tag -a <tag> <commit>`),不做 checkout
- 指定來源後,「必須在某分支上才能發版」的限制**自動解除**(那條規則的前提是「發版跟著工作目錄走」)
- **WorkflowDispatch 只能用分支或 tag** —— 這是 GitHub API 的限制,填 commit sha 會被明確擋下
- 若專案有本機步驟,而來源不是目前的 HEAD,preflight 會**警告**:步驟建置的是工作目錄,與發版來源不同

### Dry-run
Dry-run 會**實際執行所有本機步驟**,只保留不可逆的部分不做(不推 tag、不 dispatch、不建立 GitHub Release),所以它回答的是「**這次發版會不會成功**」。

因此**步驟必須是可重複執行、沒有對外副作用的**(build / pack / test)。任何不可逆的動作(例如 `dotnet nuget push`)請放進 workflow,不要放在本機步驟。

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

## Tag 管理與發版歷史
兩者刻意分開,因為能做的事不同:

- **Tag 管理** — 只列**活著的** git tag,可勾選刪除(本機 / 遠端 / GitHub Release 三個範圍各自獨立,需確認)
- **發版歷史** — 唯讀,列出本 app 發過的版本。即使 tag 與 GitHub Release 都被刪光仍保留,用來回答「v0.20.0 是什麼時候、從哪個 commit 發的」

## 資料儲存
全部位於 `%APPDATA%\PublishManager\`:

- `projects.json` — 專案設定(原子寫入 + schemaVersion)
- `releases.ndjson` — 發版帳本(append-only,一行一筆;只存事實,不存 log)
- `logs\` — 警告與錯誤的診斷紀錄(保留 14 天)

## 領域語言
`CONTEXT.md` 是這個專案的詞彙表(Release 與 GitHub Release、Stage 與 Step 等的精確定義),`docs/adr/` 記錄少數難以逆轉的決定。
