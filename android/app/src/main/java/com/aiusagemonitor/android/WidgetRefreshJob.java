package com.aiusagemonitor.android;

import android.app.job.JobInfo;
import android.app.job.JobParameters;
import android.app.job.JobScheduler;
import android.app.job.JobService;
import android.content.ComponentName;
import android.content.Context;

import java.util.List;

public final class WidgetRefreshJob extends JobService {
    private static final int JOB_ID = 500;
    static volatile boolean activityVisible;
    private volatile boolean stopped;
    private Thread thread;

    static void schedule(Context context) {
        if (!WidgetRenderer.hasWidgets(context) || activityVisible) return;
        JobScheduler scheduler = (JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (scheduler.getPendingJob(JOB_ID) != null) return;
        scheduler.schedule(new JobInfo.Builder(JOB_ID, new ComponentName(context, WidgetRefreshJob.class))
            .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).build());
    }

    static void cancelIfUnused(Context context) {
        if (WidgetRenderer.hasWidgets(context)) return;
        ((JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE)).cancel(JOB_ID);
    }

    @Override public boolean onStartJob(JobParameters params) {
        if (activityVisible || !WidgetRenderer.hasWidgets(this)) return false;
        stopped = false;
        thread = new Thread(() -> {
            try {
                AccountStore store = new AccountStore(this);
                List<AccountStore.Account> accounts = store.load();
                for (AccountStore.Account account : accounts) {
                    if (stopped || Thread.currentThread().isInterrupted() || activityVisible) break;
                    store.refresh(account);
                }
            } catch (Exception ignored) {
                // The widget retains its last saved values until the next scheduled update.
            } finally {
                WidgetRenderer.showCached(this);
                if (!stopped) jobFinished(params, false);
            }
        }, "Codex widget refresh");
        thread.start();
        return true;
    }

    @Override public boolean onStopJob(JobParameters params) {
        stopped = true;
        if (thread != null) thread.interrupt();
        return WidgetRenderer.hasWidgets(this);
    }
}
