(function ($) {
    "use strict";

    function formatBytes(value) {
        if (value === null || value === undefined || Number.isNaN(Number(value))) return "Unknown size";
        var amount = Number(value);
        if (amount < 1024) return amount.toLocaleString() + " B";
        var units = ["KB", "MB", "GB", "TB"];
        var index = -1;
        do { amount /= 1024; index++; } while (amount >= 1024 && index < units.length - 1);
        return amount.toFixed(amount >= 100 ? 0 : amount >= 10 ? 1 : 2) + " " + units[index];
    }

    function safeError(xhr, fallback) {
        if (xhr && xhr.responseJSON && xhr.responseJSON.error) return String(xhr.responseJSON.error);
        return fallback;
    }

    function antiForgeryToken() {
        return $("#download-form input[name='__RequestVerificationToken']").val()
            || $("#download-job-token input[name='__RequestVerificationToken']").val()
            || "";
    }

    function jsonCell(value) {
        if (value === null || value === undefined) return "(null)";
        if (typeof value === "string") return value;
        if (typeof value === "object") {
            try { return JSON.stringify(value); } catch (_) { return String(value); }
        }
        return String(value);
    }

    var JOB_STORAGE_KEY = "hfparquet.activeJobId";

    $(function () {
        var $fileTable = $("#file-table");
        var $downloadForm = $("#download-form");
        var activeJobId = null;
        var pollingTimer = null;

        function rememberJob(jobId) {
            activeJobId = jobId || null;
            try {
                if (jobId) window.sessionStorage.setItem(JOB_STORAGE_KEY, jobId);
                else window.sessionStorage.removeItem(JOB_STORAGE_KEY);
            } catch (_) { }
        }

        function updateSelection() {
            if (!$fileTable.length) return;
            var $items = $fileTable.find(".file-select");
            var selected = $items.filter(":checked");
            var totalBytes = 0;
            var knownSizes = true;
            selected.each(function () {
                var size = $(this).attr("data-size");
                if (size === undefined || size === "") knownSizes = false;
                else totalBytes += Number(size) || 0;
            });
            $("#select-all").prop("checked", $items.length > 0 && selected.length === $items.length);
            $("#select-all").prop("indeterminate", selected.length > 0 && selected.length < $items.length);
            $("#download-selected").prop("disabled", selected.length === 0);
            var text = selected.length + " file" + (selected.length === 1 ? "" : "s") + " selected";
            if (selected.length) text += " · " + (knownSizes ? formatBytes(totalBytes) : formatBytes(totalBytes) + "+; some sizes unknown");
            $("#selection-summary").text(text);
        }

        function terminal(status) {
            return status === "Completed" || status === "CompletedWithErrors" || status === "Cancelled" || status === "Failed";
        }

        function renderDownloadJob(job) {
            var $panel = $("#download-progress-panel");
            if (!$panel.length) return;
            $panel.prop("hidden", false);
            $("#job-status-title").text(String(job.status || "Download").replace(/([A-Z])/g, " $1").trim());
            $("#job-summary").text(job.repoId + " · " + job.files.length + " file" + (job.files.length === 1 ? "" : "s"));
            var percent = job.overallPercent;
            $("#overall-progress-bar").css("width", percent === null || percent === undefined ? (job.status === "Downloading" ? "18%" : "0%") : percent + "%");
            $("#overall-progress-text").text(percent === null || percent === undefined
                ? formatBytes(job.bytesReceived) + " downloaded · total size unknown"
                : percent + "% · " + formatBytes(job.bytesReceived) + " of " + formatBytes(job.totalBytes));
            $panel.find("[role='progressbar']").attr("aria-valuenow", percent === null || percent === undefined ? 0 : percent);

            var $body = $("#download-file-progress").empty();
            $.each(job.files || [], function (_, file) {
                var $row = $("<tr>");
                $row.append($("<td>").append($("<span>").addClass("file-name").text(file.config + "/" + file.split + "/" + file.filename)));
                $row.append($("<td>").text(file.error ? file.status + " — " + file.error : file.status));
                var progressText = file.percent === null || file.percent === undefined
                    ? formatBytes(file.bytesReceived) + " / " + formatBytes(file.totalBytes)
                    : file.percent + "% · " + formatBytes(file.bytesReceived) + " / " + formatBytes(file.totalBytes);
                var $bar = $("<span>").addClass("inline-progress").append($("<span>").css("width", file.percent === null || file.percent === undefined ? "20%" : file.percent + "%"));
                $row.append($("<td>").addClass("numeric").append($bar).append($("<span>").text(progressText)));
                $body.append($row);
            });

            var isTerminal = terminal(job.status);
            $("#cancel-download").prop("hidden", isTerminal);
            var hasRetry = isTerminal && (job.files || []).some(function (file) { return file.status === "Failed" || file.status === "Cancelled"; });
            $("#retry-download").prop("hidden", !hasRetry);
            if (job.error) $("#download-error").text(job.error).prop("hidden", false);
            else $("#download-error").prop("hidden", true).text("");
            if (isTerminal) {
                if (pollingTimer) {
                    window.clearInterval(pollingTimer);
                    pollingTimer = null;
                }
                if (job.status === "Completed") rememberJob(null);
            }
        }

        function pollJob(jobId) {
            rememberJob(jobId);
            function poll() {
                $.getJSON("/Downloads/Status", { id: jobId })
                    .done(renderDownloadJob)
                    .fail(function (xhr) {
                        if (xhr && xhr.status === 404) rememberJob(null);
                        $("#download-error").text(safeError(xhr, "Could not read download progress.")).prop("hidden", false);
                        if (pollingTimer) window.clearInterval(pollingTimer);
                        pollingTimer = null;
                    });
            }
            poll();
            if (pollingTimer) window.clearInterval(pollingTimer);
            pollingTimer = window.setInterval(poll, 1200);
        }

        $("#select-all").on("change", function () {
            $fileTable.find(".file-select").prop("checked", $(this).is(":checked"));
            updateSelection();
        });
        $fileTable.on("change", ".file-select", updateSelection);
        $("#clear-selection").on("click", function () {
            $fileTable.find(".file-select, #select-all").prop("checked", false).prop("indeterminate", false);
            updateSelection();
        });

        $downloadForm.on("submit", function (event) {
            event.preventDefault();
            if (!$fileTable.find(".file-select:checked").length) return;
            var $button = $("#download-selected").prop("disabled", true).text("Queueing…");
            $("#download-error").prop("hidden", true).text("");
            $.ajax({ url: $downloadForm.attr("action"), method: "POST", data: $downloadForm.serialize() })
                .done(function (job) { pollJob(job.jobId); })
                .fail(function (xhr) { $("#download-error").text(safeError(xhr, "Could not start the download.")).prop("hidden", false); })
                .always(function () { $button.text("Download selected"); updateSelection(); });
        });

        $("#cancel-download").on("click", function () {
            if (!activeJobId) return;
            $.ajax({ url: "/Downloads/Cancel", method: "POST", data: { id: activeJobId, __RequestVerificationToken: antiForgeryToken() } })
                .done(function () { $("#job-status-title").text("Cancelling…"); })
                .fail(function (xhr) { $("#download-error").text(safeError(xhr, "Could not cancel this job.")).prop("hidden", false); });
        });

        $("#retry-download").on("click", function () {
            if (!activeJobId) return;
            $.ajax({ url: "/Downloads/Retry", method: "POST", data: { id: activeJobId, __RequestVerificationToken: antiForgeryToken() } })
                .done(function (job) { pollJob(job.jobId); })
                .fail(function (xhr) { $("#download-error").text(safeError(xhr, "Could not retry failed files.")).prop("hidden", false); });
        });
        updateSelection();

        try {
            var savedJobId = window.sessionStorage.getItem(JOB_STORAGE_KEY);
            if (savedJobId) pollJob(savedJobId);
        } catch (_) { }

        var $remote = $("#remote-preview");
        if ($remote.length) {
            var remotePage = 1;
            var remotePageSize = Number($remote.data("page-size")) || 50;
            var remoteTotal = null;
            function renderRemote(data) {
                remotePage = Number(data.page) || remotePage;
                remoteTotal = data.totalRows === null || data.totalRows === undefined ? null : Number(data.totalRows);
                var features = data.features || [];
                var rows = data.rows || [];
                var $head = $("#remote-preview-head").empty();
                var $header = $("<tr>").append($("<th>").addClass("row-index").text("Row"));
                $.each(features, function (_, feature) {
                    var type = feature.type === undefined ? "" : " · " + jsonCell(feature.type);
                    $header.append($("<th>").text(feature.name + type));
                });
                $head.append($header);
                var $body = $("#remote-preview-body").empty();
                $.each(rows, function (_, row) {
                    var $tr = $("<tr>").append($("<td>").addClass("row-index").text(Number(row.row_idx) + 1));
                    $.each(features, function (_, feature) {
                        var full = jsonCell((row.row || {})[feature.name]);
                        var $span = $("<span>").addClass("cell-preview").text(full);
                        if (full.length > 180) $span.text(full.slice(0, 179) + "…").attr("title", full);
                        $tr.append($("<td>").append($span));
                    });
                    $body.append($tr);
                });
                if (!rows.length) $body.append($("<tr>").append($("<td>").attr("colspan", Math.max(1, features.length + 1)).text("No rows were returned for this page.")));
                var start = rows.length ? ((remotePage - 1) * remotePageSize + 1) : 0;
                var end = rows.length ? start + rows.length - 1 : 0;
                $("#remote-preview-summary").text(remoteTotal === null
                    ? rows.length + " rows in this page · total row count unavailable"
                    : remoteTotal.toLocaleString() + " total rows · showing " + start.toLocaleString() + "–" + end.toLocaleString());
                var lastPage = remoteTotal === null ? null : Math.max(1, Math.ceil(remoteTotal / remotePageSize));
                $("#remote-page-label").text(lastPage === null ? "Page " + remotePage : "Page " + remotePage + " of " + lastPage);
                $("#remote-prev").prop("disabled", remotePage <= 1);
                $("#remote-next").prop("disabled", lastPage === null ? rows.length < remotePageSize : remotePage >= lastPage);
                $("#remote-partial-warning").prop("hidden", !data.partial);
                $("#remote-preview-status").prop("hidden", true).text("");
            }
            function loadRemote(page) {
                $("#remote-preview-status").prop("hidden", false).text("Loading page " + page + "…");
                $.getJSON($remote.data("rows-url"), {
                    repoId: $remote.data("repo-id"), config: $remote.data("config"), split: $remote.data("split"), page: page, pageSize: remotePageSize
                }).done(renderRemote).fail(function (xhr) {
                    $("#remote-preview-status").prop("hidden", false).text(safeError(xhr, "The Dataset Viewer could not provide a preview. Download a shard to inspect it locally."));
                    $("#remote-preview-head, #remote-preview-body").empty();
                    $("#remote-prev, #remote-next").prop("disabled", true);
                });
            }
            $("#remote-prev").on("click", function () { if (remotePage > 1) loadRemote(remotePage - 1); });
            $("#remote-next").on("click", function () { loadRemote(remotePage + 1); });
            loadRemote(1);
        }

        var $local = $("#local-preview");
        if ($local.length && $("#local-preview-status").length) {
            var localPage = 1;
            var localPageSize = Number($local.data("page-size")) || 50;
            var localTotal = 0;
            function renderLocal(data) {
                localPage = Number(data.page) || localPage;
                localTotal = Number(data.totalRows) || 0;
                var columns = data.columns || [];
                var rows = data.rows || [];
                var $head = $("#local-preview-head").empty();
                var $header = $("<tr>").append($("<th>").addClass("row-index").text("Row"));
                $.each(columns, function (_, column) {
                    var type = column.type + (column.isNullable ? "?" : "") + (column.isRepeated ? " []" : "");
                    $header.append($("<th>").text(column.name + " · " + type));
                });
                $head.append($header);
                var $body = $("#local-preview-body").empty();
                $.each(rows, function (_, row) {
                    var $tr = $("<tr>").append($("<td>").addClass("row-index").text(Number(row.rowIndex) + 1));
                    $.each(row.cells || [], function (_, cell) {
                        var full = cell.full || "";
                        var display = cell.display || "";
                        var $span = $("<span>").addClass("cell-preview").text(display);
                        if (full !== display) $span.attr("title", full);
                        $tr.append($("<td>").append($span));
                    });
                    $body.append($tr);
                });
                if (!rows.length) $body.append($("<tr>").append($("<td>").attr("colspan", Math.max(1, columns.length + 1)).text("No rows were returned for this page.")));
                var lastPage = Math.max(1, Math.ceil(localTotal / localPageSize));
                $("#local-page-label").text("Page " + localPage + " of " + lastPage);
                $("#local-prev").prop("disabled", localPage <= 1);
                $("#local-next").prop("disabled", localPage >= lastPage);
                $("#local-preview-status").prop("hidden", true).text("");
            }
            function loadLocal(page) {
                $("#local-preview-status").prop("hidden", false).text("Reading page " + page + "…");
                $.getJSON($local.data("rows-url"), { fileId: $local.data("file-id"), page: page, pageSize: localPageSize })
                    .done(renderLocal)
                    .fail(function (xhr) { $("#local-preview-status").prop("hidden", false).text(safeError(xhr, "Could not read this Parquet page.")); });
            }
            $("#local-prev").on("click", function () { if (localPage > 1) loadLocal(localPage - 1); });
            $("#local-next").on("click", function () { if (localPage < Math.ceil(localTotal / localPageSize)) loadLocal(localPage + 1); });
            loadLocal(1);
        }
    });
})(jQuery);
