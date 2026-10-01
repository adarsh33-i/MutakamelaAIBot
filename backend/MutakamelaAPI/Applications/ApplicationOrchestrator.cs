using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using MutakamelaAPI.Browser;
using MutakamelaAPI.Models;
using MutakamelaAPI.Rules;

namespace MutakamelaAPI.Applications;

public interface IApplicationOrchestrator
{
    /// <summary>Start a job for a portal journey, seeding it with anything already known.</summary>
    Task<AIPolicyResponse> StartAsync(string sessionId, string flowId, string lang, IDictionary<string, string>? seed = null);

    /// <summary>Route a chat message to the session's active job. Returns null when no job is active.</summary>
    Task<AIPolicyResponse?> HandleMessageAsync(string sessionId, string message, string lang);

    Task<ApplicationJobResponse?> GetAsync(string jobId);
    Task<IReadOnlyList<ApplicationJobResponse>> ListAsync(string? sessionId, int limit);
    Task<ApplicationJobResponse?> ApplyDataAsync(string jobId, IDictionary<string, string> data, string lang);
    Task<ApplicationJobResponse?> ApproveAsync(string jobId, string lang);
    Task<ApplicationJobResponse?> CancelAsync(string jobId, string lang);
    Task<ApplicationJobResponse?> MarkLoginCompleteAsync(string jobId, string lang);
    Task ClearSessionAsync(string sessionId);
    ApplicationJobResponse ToResponse(ApplicationJob job, string lang);
}

/// <summary>
/// The agentic engine. It owns the job state machine and decides, deterministically,
/// what happens next: ask for a missing field, validate, pause for login/OTP,
/// run the browser agent, show the review card, and only after an explicit
/// customer approval perform the submit. The LLM is not consulted for any of
/// these decisions; it only supplies language elsewhere in the chat.
/// </summary>
public class ApplicationOrchestrator : IApplicationOrchestrator
{
    private readonly IApplicationJobStore _store;
    private readonly IFlowRegistry _flows;
    private readonly IRulesEngine _rules;
    private readonly IBrowserAgent _browser;
    private readonly ILogger<ApplicationOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _jobLocks = new();

    public ApplicationOrchestrator(
        IApplicationJobStore store,
        IFlowRegistry flows,
        IRulesEngine rules,
        IBrowserAgent browser,
        ILogger<ApplicationOrchestrator> logger)
    {
        _store = store;
        _flows = flows;
        _rules = rules;
        _browser = browser;
        _logger = logger;
    }

    // ------------------------------------------------------------------ start

    public async Task<AIPolicyResponse> StartAsync(string sessionId, string flowId, string lang, IDictionary<string, string>? seed = null)
    {
        var flow = _flows.Get(flowId);
        if (flow == null)
            return Reply("APP_FAILED", $"Unknown journey '{flowId}'.", "رحلة غير معروفة.");

        var existing = await _store.GetActiveForSessionAsync(sessionId);
        if (existing != null)
        {
            existing.TryTransition(JobStatus.Cancelled, "replaced by a new request");
            await _store.SaveAsync(existing);
        }

        var job = new ApplicationJob
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            SessionId = sessionId,
            FlowId = flow.Id,
            Language = lang
        };
        job.AddEvent("info", $"Job created for {flow.Title}");

        // Carry forward profile values from earlier completed jobs in this session.
        foreach (var prior in await _store.ListAsync(sessionId, 20))
        {
            foreach (var kv in prior.Data)
                if (flow.FindField(kv.Key) is { Source: "profile" or "document" } && !job.Data.ContainsKey(kv.Key))
                    job.Data[kv.Key] = kv.Value;
            foreach (var kv in prior.Outputs)
                if (flow.FindField(kv.Key) != null && !job.Data.ContainsKey(kv.Key) && !kv.Value.StartsWith("(simulated"))
                    job.Data[kv.Key] = kv.Value;
        }

        if (seed != null)
            foreach (var kv in seed)
                if (flow.FindField(kv.Key) != null && !string.IsNullOrWhiteSpace(kv.Value))
                    job.Data[kv.Key] = kv.Value;

        await _store.SaveAsync(job);
        var intro = IntroFor(flow, lang);
        var next = await AdvanceAsync(job, flow, lang);
        next.Response = intro.English + "\n\n" + next.Response;
        next.ResponseAr = intro.Arabic + "\n\n" + next.ResponseAr;
        return next;
    }

    // ---------------------------------------------------------------- message

    public async Task<AIPolicyResponse?> HandleMessageAsync(string sessionId, string message, string lang)
    {
        var job = await _store.GetActiveForSessionAsync(sessionId);
        if (job == null) return null;
        var flow = _flows.Get(job.FlowId);
        if (flow == null) return null;

        var gate = await Lock(job.Id);
        try
        {
            var text = message.Trim();
            var lowered = text.ToLowerInvariant();

            // A request for a different journey while this one is still collecting
            // input or waiting on the customer replaces it; mid-run jobs are not interrupted.
            var requested = PortalIntent.Detect(text);
            if (requested != null && requested != job.FlowId &&
                job.Status is JobStatus.Collecting or JobStatus.Validating or JobStatus.AwaitingLogin or JobStatus.AwaitingApproval)
            {
                if (job.PendingSwitchTo == requested)
                {
                    // Second ask for the same journey = confirmation to drop this one.
                    job.TryTransition(JobStatus.Cancelled, $"customer switched to {requested}");
                    await _store.SaveAsync(job);
                    return null;
                }
                job.PendingSwitchTo = requested;
                await _store.SaveAsync(job);
                var current = _flows.Get(job.FlowId);
                var target = _flows.Get(requested);
                return Reply(StageFor(job),
                    $"You're in the middle of \"{current?.Title}\" ({Progress(job, flow)} details collected). Switching to \"{target?.Title}\" will discard it. Reply \"switch\" to discard and start the new request, or carry on answering to continue.",
                    $"أنت الآن في منتصف \"{current?.TitleAr}\" ({Progress(job, flow)} من البيانات مكتملة). الانتقال إلى \"{target?.TitleAr}\" سيلغيه. أرسل \"تبديل\" للإلغاء والبدء بالطلب الجديد، أو تابع الإجابة للاستمرار.", job, lang);
            }
            if (job.PendingSwitchTo != null && (lowered == "switch" || lowered == "تبديل"))
            {
                var target = job.PendingSwitchTo;
                job.TryTransition(JobStatus.Cancelled, $"customer confirmed switch to {target}");
                await _store.SaveAsync(job);
                return await StartAsync(sessionId, target, lang);
            }
            job.PendingSwitchTo = null;

            if (IsCancel(lowered))
            {
                job.TryTransition(JobStatus.Cancelled, "customer cancelled");
                await _store.SaveAsync(job);
                return Reply("APP_CANCELLED",
                    "Okay, I've cancelled that request. Nothing was sent to the portal. Tell me if you'd like to start again.",
                    "حسناً، ألغيت هذا الطلب ولم يُرسل أي شيء إلى البوابة. أخبرني إذا أردت البدء من جديد.", job, lang);
            }

            if (_rules.ContainsForbiddenContent(text))
            {
                job.AddEvent("validation", "Rejected a message containing card/password-like content.");
                await _store.SaveAsync(job);
                var prompt = NextPrompt(job, flow, lang);
                return Reply(StageFor(job), "Please don't share card numbers, CVV or passwords here; they are never needed. " + prompt.English,
                    "يرجى عدم مشاركة أرقام البطاقات أو رمز الأمان أو كلمات المرور هنا؛ فهي غير مطلوبة أبداً. " + prompt.Arabic, job, lang);
            }

            switch (job.Status)
            {
                case JobStatus.Collecting:
                case JobStatus.Validating:
                    if (IsEditRequest(lowered) && MatchField(flow, lowered) is { } editTarget && job.Data.ContainsKey(editTarget.Id))
                    {
                        job.Outputs["_editing"] = editTarget.Id;
                        await _store.SaveAsync(job);
                        return Reply("APP_COLLECT", editTarget.Prompt, editTarget.PromptAr, job, lang);
                    }
                    return await HandleCollectingAsync(job, flow, text, lang);

                case JobStatus.AwaitingLogin:
                    if (IsAffirmative(lowered) || lowered.Contains("logged in") || lowered.Contains("سجلت"))
                    {
                        job.LoginCompletedAt = DateTime.UtcNow;
                        job.AddEvent("info", "Customer reported portal login complete.");
                        job.TryTransition(JobStatus.Filling, "login confirmed");
                        await _store.SaveAsync(job);
                        return await RunBrowserAsync(job, flow, lang);
                    }
                    return Reply("APP_LOGIN",
                        $"Please log in to the Mutakamela portal (Nafath or OTP) at {flow.Url} and reply \"logged in\" when done. I'll continue from there.",
                        $"يرجى تسجيل الدخول إلى بوابة متكاملة (نفاذ أو رمز التحقق) عبر {flow.Url} ثم الرد بـ \"سجلت الدخول\" وسأكمل من هناك.", job, lang);

                case JobStatus.Filling:
                    // Paused for OTP or waiting on payment confirmation.
                    if (job.Outputs.ContainsKey("payment_url"))
                    {
                        // The customer saying "paid" is a claim, not evidence. The job stays open
                        // until the browser agent reads a real policy number from the portal;
                        // no reference is ever invented here.
                        if (lowered.Contains("paid") || lowered.Contains("دفعت"))
                        {
                            job.AddEvent("info", "Customer reported payment complete; awaiting portal confirmation.");
                            var verify = await _browser.RunAsync(flow, job, e => RecordEvent(job, e), CancellationToken.None);
                            foreach (var kv in verify.Outputs) job.Outputs[kv.Key] = kv.Value;
                            if (job.Outputs.TryGetValue("policy_number", out var policy) && !string.IsNullOrWhiteSpace(policy))
                            {
                                job.ReferenceNumber = policy;
                                job.TryTransition(JobStatus.Done, "policy number confirmed on portal");
                                await _store.SaveAsync(job);
                                return Reply("APP_DONE", OutputsText(flow, job, "en"), OutputsText(flow, job, "ar"), job, lang);
                            }
                            await _store.SaveAsync(job);
                            return Reply("APP_PAYMENT",
                                "Thanks. I can't see a policy number on the portal yet; it can take a few minutes after payment. I'll keep this request open — reply \"paid\" again later and I'll check, or send \"cancel\" if you didn't complete it.",
                                "شكراً. لا أرى رقم وثيقة على البوابة بعد؛ قد يستغرق ذلك بضع دقائق بعد الدفع. سأبقي الطلب مفتوحاً — أرسل \"دفعت\" لاحقاً وسأتحقق، أو \"إلغاء\" إن لم تكمل الدفع.", job, lang);
                        }
                        return PaymentReply(job, flow, lang);
                    }
                    if (flow.FindField("otp_code") is { } otpField && !job.Data.ContainsKey("otp_code"))
                    {
                        var problem = _rules.Validate(otpField, text, out var code);
                        if (problem != null)
                            return Reply("APP_OTP", problem.Message, problem.MessageAr, job, lang);
                        job.Data["otp_code"] = code;
                        job.AddEvent("field", "One-time code received from customer.");
                        return await RunBrowserAsync(job, flow, lang);
                    }
                    return Reply("APP_WORKING", "I'm still working on the portal for you. One moment.", "ما زلت أعمل على البوابة من أجلك. لحظة من فضلك.", job, lang);

                case JobStatus.AwaitingApproval:
                    if (IsEditRequest(lowered))
                    {
                        var field = MatchField(flow, lowered);
                        if (field == null)
                        {
                            job.Outputs["_editing"] = "choose";
                            await _store.SaveAsync(job);
                            return Reply("APP_EDIT", "Which detail would you like to change? " + FieldMenu(flow, job, "en"),
                                "ما المعلومة التي تريد تعديلها؟ " + FieldMenu(flow, job, "ar"), job, lang);
                        }
                        job.Outputs["_editing"] = field.Id;
                        job.TryTransition(JobStatus.Collecting, $"editing {field.Id}");
                        await _store.SaveAsync(job);
                        return Reply("APP_COLLECT", field.Prompt, field.PromptAr, job, lang);
                    }
                    if (job.Outputs.TryGetValue("_editing", out var editing) && editing == "choose")
                    {
                        var field = MatchField(flow, lowered);
                        if (field != null)
                        {
                            job.Outputs["_editing"] = field.Id;
                            job.TryTransition(JobStatus.Collecting, $"editing {field.Id}");
                            await _store.SaveAsync(job);
                            return Reply("APP_COLLECT", field.Prompt, field.PromptAr, job, lang);
                        }
                    }
                    if (IsApproval(lowered))
                    {
                        var approved = await ApproveInternalAsync(job, flow, lang);
                        return approved;
                    }
                    return ReviewReply(job, flow, lang,
                        "Please reply Confirm to submit, Edit to change a detail, or Cancel.",
                        "يرجى الرد بـ تأكيد للإرسال، أو تعديل لتغيير معلومة، أو إلغاء.");

                case JobStatus.Submitting:
                    return Reply("APP_WORKING", "Submitting now, please wait.", "جارٍ الإرسال، يرجى الانتظار.", job, lang);

                default:
                    return null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AIPolicyResponse> HandleCollectingAsync(ApplicationJob job, FlowSpec flow, string text, string lang)
    {
        FieldSpec? target = null;
        if (job.Outputs.TryGetValue("_editing", out var editingId) && editingId != "choose")
            target = flow.FindField(editingId);
        target ??= NextMissingField(job, flow);

        if (target == null)
            return await AdvanceAsync(job, flow, lang);

        // Opportunistically pick up extra values from a long first message.
        var extracted = ExtractKnownValues(flow, job, text);
        var problem = _rules.Validate(target, text, out var normalized);
        if (problem != null)
        {
            // If the message was clearly multi-field and the target was found by extraction, accept that.
            if (extracted.TryGetValue(target.Id, out var fromExtract))
            {
                normalized = fromExtract;
            }
            else
            {
                job.AddEvent("validation", $"{target.Id}: {problem.Message}");
                await _store.SaveAsync(job);
                return Reply("APP_COLLECT", $"{problem.Message} {target.Prompt}", $"{problem.MessageAr} {target.PromptAr}", job, lang);
            }
        }

        StoreValue(job, target, normalized);
        foreach (var kv in extracted) job.Data.TryAdd(kv.Key, kv.Value);
        job.AddEvent("field", $"Collected {target.Id}" + (target.Sensitive ? string.Empty : $" = {normalized}"));
        job.Outputs.Remove("_editing");
        await _store.SaveAsync(job);
        return await AdvanceAsync(job, flow, lang);
    }

    // ---------------------------------------------------------------- advance

    /// <summary>Decide the next step from the job's current data and status.</summary>
    private async Task<AIPolicyResponse> AdvanceAsync(ApplicationJob job, FlowSpec flow, string lang)
    {
        var missing = NextMissingField(job, flow);
        if (missing != null)
        {
            if (job.Status == JobStatus.Validating) job.TryTransition(JobStatus.Collecting, "more data needed");
            await _store.SaveAsync(job);
            var progress = Progress(job, flow);
            return Reply("APP_COLLECT", $"{missing.Prompt} ({progress})", $"{missing.PromptAr} ({progress})", job, lang);
        }

        if (job.Status == JobStatus.Collecting) job.TryTransition(JobStatus.Validating, "all required fields present");
        var (problems, _) = _rules.ValidateAll(flow, job.Data);
        if (problems.Count > 0)
        {
            job.TryTransition(JobStatus.Collecting, "validation failed");
            var first = problems[0];
            var field = flow.FindField(first.Field)!;
            job.Outputs["_editing"] = field.Id;
            await _store.SaveAsync(job);
            return Reply("APP_COLLECT", $"{first.Message} {field.Prompt}", $"{first.MessageAr} {field.PromptAr}", job, lang);
        }

        if (flow.RequiresLogin && job.LoginCompletedAt == null)
        {
            job.TryTransition(JobStatus.AwaitingLogin, "data valid; portal login needed");
            await _store.SaveAsync(job);
            return Reply("APP_LOGIN",
                $"Everything checks out. Please log in to the Mutakamela portal at {flow.Url} (Nafath or OTP). Reply \"logged in\" when you're done and I'll fill it in for you. I can't log in on your behalf.",
                $"كل شيء صحيح. يرجى تسجيل الدخول إلى بوابة متكاملة عبر {flow.Url} (نفاذ أو رمز التحقق). رد بـ \"سجلت الدخول\" عند الانتهاء وسأعبئ البيانات نيابة عنك. لا يمكنني تسجيل الدخول بدلاً منك.", job, lang);
        }

        job.TryTransition(JobStatus.Filling, "data valid; starting portal run");
        await _store.SaveAsync(job);
        return await RunBrowserAsync(job, flow, lang);
    }

    private async Task<AIPolicyResponse> RunBrowserAsync(ApplicationJob job, FlowSpec flow, string lang)
    {
        FlowRunResult result;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            result = await _browser.RunAsync(flow, job, e => RecordEvent(job, e), cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Browser agent crashed for job {Job}", job.Id);
            result = new FlowRunResult { Succeeded = false, Error = "The browser agent stopped unexpectedly.", ErrorAr = "توقف وكيل المتصفح بشكل غير متوقع." };
        }

        foreach (var kv in result.Outputs) job.Outputs[kv.Key] = kv.Value;
        if (result.FinalScreenshotPath != null) job.Outputs["_final_screenshot"] = result.FinalScreenshotPath;

        if (!result.Succeeded)
        {
            job.FailureReason = result.Error;
            job.FailureReasonAr = result.ErrorAr;
            job.TryTransition(JobStatus.Failed, result.Error);
            await _store.SaveAsync(job);
            return Reply("APP_FAILED",
                $"I couldn't complete this on the portal: {result.Error} Nothing was submitted. You can continue manually at {flow.Url}.",
                $"لم أتمكن من إكمال ذلك على البوابة: {result.ErrorAr ?? result.Error} لم يُرسل أي شيء. يمكنك المتابعة يدوياً عبر {flow.Url}.", job, lang);
        }

        switch (result.PausedFor)
        {
            case "login":
                job.TryTransition(JobStatus.AwaitingLogin, "portal requested login");
                await _store.SaveAsync(job);
                return Reply("APP_LOGIN",
                    $"The portal needs you to log in first. Please sign in at {flow.Url} and reply \"logged in\".",
                    $"تطلب البوابة تسجيل الدخول أولاً. يرجى الدخول عبر {flow.Url} ثم الرد بـ \"سجلت الدخول\".", job, lang);

            case "otp":
                await _store.SaveAsync(job);
                return Reply("APP_OTP",
                    "The portal sent a verification code to your mobile. Please type it here and I'll enter it for you.",
                    "أرسلت البوابة رمز تحقق إلى جوالك. اكتبه هنا وسأدخله نيابة عنك.", job, lang);

            case "payment":
                await _store.SaveAsync(job);
                return PaymentReply(job, flow, lang);

            case "approval":
                job.TryTransition(JobStatus.AwaitingApproval, "portal filled; awaiting customer approval");
                await _store.SaveAsync(job);
                return ReviewReply(job, flow, lang,
                    "I've filled everything in on the portal but have NOT submitted it. Please review and reply Confirm to submit, or Edit to change something.",
                    "عبأت كل البيانات في البوابة لكن لم أرسلها بعد. يرجى المراجعة والرد بـ تأكيد للإرسال أو تعديل لتغيير شيء.");

            default:
                job.ReferenceNumber = job.Outputs.GetValueOrDefault("claim_number") ?? job.Outputs.GetValueOrDefault("policy_number");
                job.TryTransition(JobStatus.Done, "flow finished");
                await _store.SaveAsync(job);
                return Reply("APP_DONE", OutputsText(flow, job, "en"), OutputsText(flow, job, "ar"), job, lang);
        }
    }

    // ---------------------------------------------------------------- approve

    private async Task<AIPolicyResponse> ApproveInternalAsync(ApplicationJob job, FlowSpec flow, string lang)
    {
        if (job.SubmitAttempted)
        {
            return Reply(StageFor(job), "This request was already submitted; I won't send it twice.", "تم إرسال هذا الطلب مسبقاً؛ لن أرسله مرتين.", job, lang);
        }

        job.ApprovedAt = DateTime.UtcNow;
        job.IdempotencyKey ??= $"{job.Id}-{Guid.NewGuid():N}"[..32];
        job.SubmitAttempted = true;
        job.TryTransition(JobStatus.Submitting, "customer approved");
        await _store.SaveAsync(job);

        FlowRunResult result;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            result = await _browser.SubmitAsync(flow, job, e => RecordEvent(job, e), cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Submit crashed for job {Job}", job.Id);
            result = new FlowRunResult { Succeeded = false, Error = "The submit step failed.", ErrorAr = "فشلت خطوة الإرسال." };
        }

        foreach (var kv in result.Outputs) job.Outputs[kv.Key] = kv.Value;
        if (!result.Succeeded)
        {
            job.FailureReason = result.Error;
            job.FailureReasonAr = result.ErrorAr;
            job.TryTransition(JobStatus.Failed, result.Error);
            await _store.SaveAsync(job);
            return Reply("APP_FAILED",
                $"The submission did not go through: {result.Error} Please check the portal before retrying so it isn't sent twice.",
                $"لم يتم الإرسال: {result.ErrorAr ?? result.Error} يرجى التحقق من البوابة قبل إعادة المحاولة حتى لا يُرسل مرتين.", job, lang);
        }

        job.ReferenceNumber = job.Outputs.GetValueOrDefault("claim_number") ?? job.Outputs.GetValueOrDefault("policy_number");
        job.TryTransition(JobStatus.Done, "submitted");
        await _store.SaveAsync(job);
        return Reply("APP_DONE", OutputsText(flow, job, "en"), OutputsText(flow, job, "ar"), job, lang);
    }

    // ---------------------------------------------------------------- API ops

    public async Task<ApplicationJobResponse?> GetAsync(string jobId)
    {
        var job = await _store.GetAsync(jobId);
        return job == null ? null : ToResponse(job, job.Language);
    }

    public async Task<IReadOnlyList<ApplicationJobResponse>> ListAsync(string? sessionId, int limit) =>
        (await _store.ListAsync(sessionId, limit)).Select(j => ToResponse(j, j.Language)).ToList();

    public async Task<ApplicationJobResponse?> ApplyDataAsync(string jobId, IDictionary<string, string> data, string lang)
    {
        var job = await _store.GetAsync(jobId);
        if (job == null) return null;
        var flow = _flows.Get(job.FlowId);
        if (flow == null || job.IsTerminal) return ToResponse(job, lang);
        var gate = await Lock(job.Id);
        try
        {
            foreach (var kv in data)
            {
                var field = flow.FindField(kv.Key);
                if (field == null) continue;
                var problem = _rules.Validate(field, kv.Value, out var normalized);
                if (problem == null) StoreValue(job, field, normalized);
            }
            if (job.Status == JobStatus.AwaitingApproval) job.TryTransition(JobStatus.Collecting, "data edited via API");
            await AdvanceAsync(job, flow, lang);
            return ToResponse(job, lang);
        }
        finally { gate.Release(); }
    }

    public async Task<ApplicationJobResponse?> ApproveAsync(string jobId, string lang)
    {
        var job = await _store.GetAsync(jobId);
        if (job == null) return null;
        var flow = _flows.Get(job.FlowId);
        if (flow == null || job.Status != JobStatus.AwaitingApproval) return ToResponse(job, lang);
        var gate = await Lock(job.Id);
        try { await ApproveInternalAsync(job, flow, lang); return ToResponse(job, lang); }
        finally { gate.Release(); }
    }

    public async Task<ApplicationJobResponse?> CancelAsync(string jobId, string lang)
    {
        var job = await _store.GetAsync(jobId);
        if (job == null) return null;
        if (job.TryTransition(JobStatus.Cancelled, "cancelled via API")) await _store.SaveAsync(job);
        await _browser.CloseAsync(job.Id);
        return ToResponse(job, lang);
    }

    public async Task<ApplicationJobResponse?> MarkLoginCompleteAsync(string jobId, string lang)
    {
        var job = await _store.GetAsync(jobId);
        if (job == null) return null;
        var flow = _flows.Get(job.FlowId);
        if (flow == null || job.Status != JobStatus.AwaitingLogin) return ToResponse(job, lang);
        var gate = await Lock(job.Id);
        try
        {
            job.LoginCompletedAt = DateTime.UtcNow;
            job.TryTransition(JobStatus.Filling, "login confirmed via API");
            await _store.SaveAsync(job);
            await RunBrowserAsync(job, flow, lang);
            return ToResponse(job, lang);
        }
        finally { gate.Release(); }
    }

    public Task ClearSessionAsync(string sessionId) => _store.DeleteForSessionAsync(sessionId);

    public ApplicationJobResponse ToResponse(ApplicationJob job, string lang)
    {
        var flow = _flows.Get(job.FlowId);
        var (problems, missing) = flow == null ? (new List<FieldProblem>(), new List<string>()) : _rules.ValidateAll(flow, job.Data);
        var (msg, msgAr) = StatusText(job, flow);
        return new ApplicationJobResponse
        {
            Id = job.Id,
            SessionId = job.SessionId,
            FlowId = job.FlowId,
            Status = job.Status,
            StatusMessage = msg,
            StatusMessageAr = msgAr,
            MissingFields = missing,
            ValidationErrors = problems,
            Data = job.Data.Where(kv => flow?.FindField(kv.Key)?.Sensitive != true).ToDictionary(kv => kv.Key, kv => kv.Value),
            Outputs = job.Outputs.Where(kv => !kv.Key.StartsWith('_')).ToDictionary(kv => kv.Key, kv => kv.Value),
            ReferenceNumber = job.ReferenceNumber,
            FailureReason = job.FailureReason,
            FailureReasonAr = job.FailureReasonAr,
            Review = flow == null ? null : BuildReview(job, flow),
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            Events = job.Events.TakeLast(30).ToList()
        };
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Persist a normalised value. Date-of-birth normalisation carries the calendar
    /// the customer used ("yyyy-MM-dd|hijri"); keep the Gregorian date in the field and
    /// the calendar in a sibling key so re-validation and the portal fill both work.
    /// </summary>
    private static void StoreValue(ApplicationJob job, FieldSpec field, string normalized)
    {
        if (field.Rule == "date_of_birth" && normalized.Contains('|'))
        {
            var parts = normalized.Split('|', 2);
            job.Data[field.Id] = parts[0];
            job.Data[field.Id + "_calendar"] = parts[1];
            return;
        }
        job.Data[field.Id] = normalized;
    }

    private async Task RecordEvent(ApplicationJob job, FlowStepEvent e)
    {
        job.AddEvent(e.Type, e.Message, e.Screen, e.ScreenshotPath);
        if (e.Outputs != null) foreach (var kv in e.Outputs) job.Outputs[kv.Key] = kv.Value;
        await _store.SaveAsync(job);
    }

    private async Task<SemaphoreSlim> Lock(string jobId)
    {
        var gate = _jobLocks.GetOrAdd(jobId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return gate;
    }

    private FieldSpec? NextMissingField(ApplicationJob job, FlowSpec flow) =>
        flow.AllFields.FirstOrDefault(f =>
            f.Type != "file" && f.Type != "otp" && f.Source != "portal" &&
            _rules.IsRequired(f, job.Data) &&
            (!job.Data.TryGetValue(f.Id, out var v) || string.IsNullOrWhiteSpace(v)));

    private (string English, string Arabic) NextPrompt(ApplicationJob job, FlowSpec flow, string lang)
    {
        var next = NextMissingField(job, flow);
        return next == null ? ("", "") : (next.Prompt, next.PromptAr);
    }

    private string Progress(ApplicationJob job, FlowSpec flow)
    {
        var required = flow.AllFields.Where(f => f.Type != "file" && f.Type != "otp" && f.Source != "portal" && _rules.IsRequired(f, job.Data)).ToList();
        var done = required.Count(f => job.Data.TryGetValue(f.Id, out var v) && !string.IsNullOrWhiteSpace(v));
        return $"{done}/{required.Count}";
    }

    /// <summary>Regex pickup of unambiguous values (ID, mobile, email, dates) so a chatty first message isn't wasted.</summary>
    private Dictionary<string, string> ExtractKnownValues(FlowSpec flow, ApplicationJob job, string text)
    {
        var found = new Dictionary<string, string>();
        void Try(string fieldId, string pattern)
        {
            var field = flow.FindField(fieldId);
            if (field == null || job.Data.ContainsKey(fieldId)) return;
            var m = Regex.Match(text, pattern);
            if (m.Success && _rules.Validate(field, m.Value, out var normalized) == null) found[fieldId] = normalized;
        }
        Try("national_id", @"\b[12]\d{9}\b");
        Try("mobile", @"(\+?966|0)?5\d{8}\b");
        Try("email", @"[^@\s]+@[^@\s]+\.[^@\s]{2,}");
        // Only accept prefixed references that contain at least one digit, so ordinary
        // uppercase words ("PLEASE") are never mistaken for a reference.
        Try("policy_number", @"\b(?:POL|MTK)[-/]?(?=[A-Z0-9-]*\d)[A-Z0-9-]{4,}\b");
        Try("claim_number", @"\b(?:CLM|CL)[-/]?(?=[A-Z0-9-]*\d)[A-Z0-9-]{4,}\b");
        return found;
    }

    private static bool IsCancel(string m) => m is "cancel" or "stop" or "إلغاء" or "الغاء" || m.StartsWith("cancel ") || m.Contains("never mind");
    private static bool IsAffirmative(string m) => m is "yes" or "y" or "done" or "ok" or "okay" or "نعم" or "تم" or "اي";
    // Deliberately excludes bare affirmatives ("yes", "ok"): submitting to the insurer
    // requires an unambiguous word so a stray "yes" can never send an application.
    private static bool IsApproval(string m) => m.StartsWith("confirm") || m == "approve" || m == "submit" || m.StartsWith("تأكيد") || m == "اعتمد" || m == "ارسال" || m == "إرسال";
    private static bool IsEditRequest(string m) => m.StartsWith("edit") || m.StartsWith("change") || m.StartsWith("تعديل") || m.StartsWith("غير");

    private static FieldSpec? MatchField(FlowSpec flow, string lowered) =>
        flow.AllFields.FirstOrDefault(f =>
            lowered.Contains(f.Id.Replace('_', ' ')) ||
            lowered.Contains(f.Label.ToLowerInvariant()) ||
            (!string.IsNullOrEmpty(f.LabelAr) && lowered.Contains(f.LabelAr)));

    private string FieldMenu(FlowSpec flow, ApplicationJob job, string lang) =>
        string.Join(lang == "ar" ? "، " : ", ", flow.AllFields
            .Where(f => job.Data.ContainsKey(f.Id) && !f.Sensitive)
            .Select(f => lang == "ar" ? f.LabelAr : f.Label));

    private static string StageFor(ApplicationJob job) => job.Status switch
    {
        JobStatus.Collecting or JobStatus.Validating => "APP_COLLECT",
        JobStatus.AwaitingLogin => "APP_LOGIN",
        JobStatus.Filling => "APP_WORKING",
        JobStatus.AwaitingApproval => "APP_REVIEW",
        JobStatus.Submitting => "APP_WORKING",
        JobStatus.Done => "APP_DONE",
        JobStatus.Failed => "APP_FAILED",
        _ => "APP_CANCELLED"
    };

    private static (string English, string Arabic) IntroFor(FlowSpec flow, string lang) => flow.Id switch
    {
        FlowIds.TrackAClaim => ("I'll check your claim status on the Mutakamela portal.", "سأتحقق من حالة مطالبتك على بوابة متكاملة."),
        FlowIds.MakeAClaim => ("I'll prepare your motor claim and fill it in on the Mutakamela portal. You'll review everything before anything is submitted.", "سأجهّز مطالبة المركبات وأعبئها على بوابة متكاملة. ستراجع كل شيء قبل الإرسال."),
        FlowIds.BuyMotorInsurance => ("Let's get you a motor insurance quote. I'll fill the portal for you and stop at the payment page, which you complete yourself.", "لنحصل على عرض سعر لتأمين المركبات. سأعبئ البوابة نيابة عنك وأتوقف عند صفحة الدفع التي تكملها بنفسك."),
        FlowIds.PersonalInfo => ("I'll update your personal details on the portal. You'll confirm before anything is saved.", "سأحدّث بياناتك الشخصية على البوابة. ستؤكد قبل حفظ أي شيء."),
        _ => ($"Starting: {flow.Title}.", $"بدء: {flow.TitleAr}.")
    };

    private (string, string) StatusText(ApplicationJob job, FlowSpec? flow) => job.Status switch
    {
        JobStatus.Collecting => ("Collecting details", "جمع البيانات"),
        JobStatus.Validating => ("Checking details", "التحقق من البيانات"),
        JobStatus.AwaitingLogin => ("Waiting for you to log in to the portal", "بانتظار تسجيل دخولك إلى البوابة"),
        JobStatus.Filling => job.Outputs.ContainsKey("payment_url") ? ("Waiting for payment", "بانتظار الدفع") : ("Filling the portal", "تعبئة البوابة"),
        JobStatus.AwaitingApproval => ("Waiting for your approval", "بانتظار موافقتك"),
        JobStatus.Submitting => ("Submitting", "جارٍ الإرسال"),
        JobStatus.Done => ("Done", "اكتمل"),
        JobStatus.Failed => ("Failed safely — nothing submitted", "فشل بأمان — لم يُرسل شيء"),
        _ => ("Cancelled", "ملغى")
    };

    private ReviewSummary BuildReview(ApplicationJob job, FlowSpec flow)
    {
        var review = new ReviewSummary
        {
            FinalScreenshotPath = job.Outputs.GetValueOrDefault("_final_screenshot"),
            Disclaimer = _browser.Name == "simulated"
                ? "Simulation mode: the portal was not contacted. Values marked (simulated) are placeholders."
                : "Nothing is submitted until you confirm.",
            DisclaimerAr = _browser.Name == "simulated"
                ? "وضع المحاكاة: لم يتم الاتصال بالبوابة. القيم الموسومة (simulated) تجريبية."
                : "لن يُرسل أي شيء حتى تؤكد."
        };
        foreach (var field in flow.AllFields)
        {
            if (field.Sensitive || !job.Data.TryGetValue(field.Id, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            review.Lines.Add(new ReviewLine { Field = field.Id, Label = field.Label, LabelAr = field.LabelAr, Value = value, Source = field.Source });
        }
        foreach (var output in flow.Outputs)
        {
            if (!job.Outputs.TryGetValue(output.Id, out var value)) continue;
            review.Lines.Add(new ReviewLine { Field = output.Id, Label = output.Label, LabelAr = output.LabelAr, Value = value, Source = "portal" });
        }
        return review;
    }

    private AIPolicyResponse ReviewReply(ApplicationJob job, FlowSpec flow, string lang, string lead, string leadAr)
    {
        var review = BuildReview(job, flow);
        var en = new StringBuilder(lead).AppendLine().AppendLine();
        var ar = new StringBuilder(leadAr).AppendLine().AppendLine();
        foreach (var line in review.Lines)
        {
            en.AppendLine($"• {line.Label}: {line.Value}");
            ar.AppendLine($"• {line.LabelAr}: {line.Value}");
        }
        en.AppendLine().Append(review.Disclaimer);
        ar.AppendLine().Append(review.DisclaimerAr);
        return Reply("APP_REVIEW", en.ToString().TrimEnd(), ar.ToString().TrimEnd(), job, lang);
    }

    private AIPolicyResponse PaymentReply(ApplicationJob job, FlowSpec flow, string lang)
    {
        var url = job.Outputs.GetValueOrDefault("payment_url", flow.Url);
        var quotes = job.Outputs.GetValueOrDefault("quote_options");
        var en = (quotes == null ? "" : $"Quotes found: {quotes}\n\n") +
                 $"Your application is filled in. Please complete payment securely on the portal: {url}\nI never see or enter card details. Reply \"paid\" once done and I'll record the policy reference.";
        var ar = (quotes == null ? "" : $"عروض الأسعار: {quotes}\n\n") +
                 $"تم تعبئة طلبك. يرجى إكمال الدفع بأمان عبر البوابة: {url}\nلا أطّلع على بيانات البطاقة ولا أدخلها أبداً. رد بـ \"دفعت\" عند الانتهاء وسأسجل مرجع الوثيقة.";
        return Reply("APP_PAYMENT", en, ar, job, lang);
    }

    private string OutputsText(FlowSpec flow, ApplicationJob job, string lang)
    {
        var sb = new StringBuilder();
        sb.AppendLine(lang == "ar" ? $"اكتمل: {flow.TitleAr}." : $"Done: {flow.Title}.");
        foreach (var output in flow.Outputs)
            if (job.Outputs.TryGetValue(output.Id, out var v))
                sb.AppendLine($"• {(lang == "ar" ? output.LabelAr : output.Label)}: {v}");
        if (_browser.Name == "simulated")
            sb.Append(lang == "ar" ? "(وضع المحاكاة — لم يتم الاتصال بالبوابة الفعلية.)" : "(Simulation mode — the live portal was not contacted.)");
        return sb.ToString().TrimEnd();
    }

    private AIPolicyResponse Reply(string stage, string en, string ar, ApplicationJob? job = null, string lang = "en") => new()
    {
        Stage = stage,
        Intent = "APPLICATION",
        DetectedLob = "MOTOR",
        Response = en,
        ResponseAr = ar,
        NextAction = stage,
        Application = job == null ? null : ToResponse(job, lang)
    };
}
