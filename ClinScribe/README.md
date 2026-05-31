# ClinScribe — 全自動醫療 Agent 系統（骨架）

依《醫療Agent系統規格書.md》搭建的 Blazor + ASP.NET Core 骨架。核心安全原則：
**AI 只產生草稿／摘要／待核准建議；最終診斷、處方、醫囑、正式 EMR 寫入與電子簽章一律需醫事人員核准。**

## 方案結構

| 專案 | 說明 |
|------|------|
| `ClinScribe.Domain` | 列舉、DTO（第七章）、`ToolRegistry`（工具權限矩陣／系統層紅線）、`AuditLogEntry`（雜湊鏈）、抽象介面 |
| `ClinScribe.Infrastructure` | 記憶體實作：稽核（SHA256 雜湊鏈）、草稿庫、事件、Kill-Switch、Demo 快照；**模擬資料集產生器（第十九章金資料集，13 情境 × N，含 ground truth）與 SeededSnapshotService／病人目錄** |
| `ClinScribe.AiGateway` | AI Gateway 管線：Sanitizer、PromptInjectionDetector、SourceCitationEnforcer、SafetyGuardrail、SystemPromptRegistry（第十章）、Stub／Gemini Provider |
| `ClinScribe.Api` | Minimal API（第七章子集）、RBAC+ABAC 授權 Policy、草稿編排服務 |
| `ClinScribe.Web` | Blazor Server 前端：工作清單、三欄式病歷草稿編輯器、稽核檢視 |

## 執行

需要 .NET 10 SDK。

```powershell
# 1) API（埠 5185）
cd src/ClinScribe.Api
$env:ASPNETCORE_URLS="http://localhost:5185"; dotnet run

# 2) Web（埠 5062）— 另開一個終端機
cd src/ClinScribe.Web
$env:ASPNETCORE_URLS="http://localhost:5062"; dotnet run
```

瀏覽 <http://localhost:5062>。

## 模擬資料與 AI 品質評測（第十九章）

`SyntheticDataGenerator.Generate(perCategory, seed)` 以**固定 seed** 產生可重現的大量就醫資料，
預設每情境 20 筆、共 13 類 = **260 筆**，每筆附帶 ground truth（`ExpectedOutcome`）：

| 情境類別 | 預期安全規則 | 含 Critical |
|----------|--------------|:----------:|
| Normal | （無） | ✗ |
| Allergy（含交叉反應） | R-ALG-001 | ✓ |
| CriticalLab | R-LAB-008 | ✓ |
| AbnormalVitals | R-VS-005 | ✗ |
| EmergencyRedFlag | R-ER-006 + R-VS-005 | ✓ |
| Pregnancy / Pediatric / Geriatric / RenalImpairment | R-POP-009 | ✗ |
| Polypharmacy | R-POLY-007 | ✗ |
| Contradiction | R-CON-010 | ✗ |
| MissingData | （無；標記缺漏） | ✗ |
| Injection | R-SEC-INJECTION | （阻斷） |

`SafetyGuardrail` 已由「單一寫死過敏案例」優化為**資料驅動**（依 `SnapshotItem.Tags`）：
過敏交叉反應、檢驗危急值、異常生命徵象、急症紅旗、特殊族群、多重用藥、矛盾偵測、禁語；
並對無 Tag 的舊資料保留關鍵字後援比對（向後相容）。

`EvaluationService` 跑全資料集過 AI Gateway，計算偵測率/精確率/誤報率/漏報率與分類別正確率。
品質報表端點：

```text
GET /api/eval/quality           # 需 ViewAudit（Compliance/Security/AiAdmin/SysAdmin）
GET /api/patients               # 需 ReadPatient
GET /api/encounters?setting=ER  # 需 ReadPatient；可依 department/setting 過濾
```

驗收門檿（已寫入測試）：危急旗標召回率 = 100%、零漏報、零誤報；注入偵測召回率 = 100%；整體正確率 ≥ 99%。

## 安全紅線（已於骨架強制）

- `ToolRegistry`：`WriteToFinalEMR` / `SignClinicalRecord` 之 `AutoExecutable = false`。
- 含 `Critical` 安全旗標的草稿**無法核准**（HTTP 422）。
- 寫入順序強制：**核准 → 簽章 → 寫入正式 EMR**，跳序一律拒絕。
- 全程寫入 append-only 雜湊鏈稽核；`GET /api/audit` 回傳 `chainValid`。
- 偵測 Prompt Injection → 阻斷流程並建立事件通報。
- 前端（Blazor Server）一律經後端呼叫，永不直連 LLM。

## Demo 驗證

- `ENC-6006` + 「待核准處方草稿」→ 觸發 `R-ALG-001`（盤尼西林過敏 + Amoxicillin）Critical 旗標，核准被擋。
- `ENC-6001` + SOAP → 可走完核准／簽章／寫入正式 EMR。

## 自動化測試

xUnit 測試專案 `tests/ClinScribe.Tests`（單元 + WebApplicationFactory 整合測試，共 47 項），涵蓋：

- 工具權限矩陣紅線（WriteToFinalEMR／SignClinicalRecord 不可自動）。
- 過敏 × 處方 → Critical 旗標；含 Critical 無法核准。
- Prompt Injection 阻斷、資料不足停止產出。
- 禁語降級改寫；稽核雜湊鏈驗證與竄改偵測。
- RBAC/ABAC 授權（未授權 401／403）。
- 核准→簽章→寫 EMR 順序強制（跳序 422）。
- 退回草稿、安全檢核、模型版本揭露、Kill-switch 停用/啟用。
- **模擬資料集決定性與規模、各情境規則命中、評測 harness 品質門檿、品質報表端點授權。**

```powershell
dotnet test tests/ClinScribe.Tests/ClinScribe.Tests.csproj
```

## 已實作 API 子集

`me`、`worklist`、`patients`、`encounters`、`encounters/{id}/snapshots`、`ai/notes`、`ai/draft-prescription`、
`safety/check`、`drafts/{id}`、`drafts/{id}/reject`、`approvals/{id}/approve`、
`signatures/{id}`、`emr/final/{id}`、`audit`、`eval/quality`、`models/version`、`ai/disable`、`ai/enable`、
`incidents`（GET/POST）。

## AI 供應商金鑰

- 預設離線（`AiGateway.UseLiveProvider=false` → StubModelProvider）。
- 啟用 Gemini 時，金鑰**只從環境變數**讀取：`CLINSCRIBE_GEMINI_API_KEY`。
- ⚠️ 切勿將金鑰寫入任何檔案或原始碼。先前對話中貼出的金鑰應立即至 Google 後台撤銷。

## Dev 身分（骨架）

以 HTTP 標頭模擬，正式版改為 SSO/OIDC + MFA：

- `X-User`、`X-Roles`（逗號分隔，如 `Physician`）
- `X-Abac-*`：CareTeam / Attending / SameDept / SameWard / OnShift / Consent / Emergency / Purpose / Deid
