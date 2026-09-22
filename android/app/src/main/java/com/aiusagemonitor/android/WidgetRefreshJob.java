package com.aiusagemonitor.android;

import android.app.job.JobInfo;
import android.app.job.JobParameters;
import android.app.job.JobScheduler;
import android.app.job.JobService;
import android.content.ComponentName;
import android.content.Context;

import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;

public final class WidgetRefreshJob extends JobService {
    private static final int IMMEDIATE_JOB_ID = 500;
    private static final int PERIODIC_JOB_ID = 501;
    private static final AtomicBoolean ACTIVE = new AtomicBoolean();
    static volatile boolean activityVisible;
    private volatile boolean stopped;
    private Thread thread;

    static void scheduleImmediate(Context context) {
        if (!WidgetRenderer.hasWidgets(context) || activityVisible) return;
        JobScheduler scheduler = (JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (scheduler.getPendingJob(IMMEDIATE_JOB_ID) != null) return;
        scheduler.schedule(new JobInfo.Builder(IMMEDIATE_JOB_ID, new ComponentName(context, WidgetRefreshJob.class))
            .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).build());
    }

    static boolean schedulePeriodic(Context context) {
        JobScheduler scheduler = (JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (!WidgetRenderer.hasWidgets(context)) {
            scheduler.cancel(PERIODIC_JOB_ID);
            return true;
        }
        long interval = WidgetSettings.read(context) * 60_000L;
        JobInfo pending = scheduler.getPendingJob(PERIODIC_JOB_ID);
        if (pending != null && pending.getIntervalMillis() == interval) return true;
        JobInfo job = new JobInfo.Builder(PERIODIC_JOB_ID,
            new ComponentName(context, WidgetRefreshJob.class))
            .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
            .setPeriodic(interval, JobInfo.getMinFlexMillis())
            .setPersisted(true).build();
        return scheduler.schedule(job) == JobScheduler.RESULT_SUCCESS;
    }

    static void cancelIfUnused(Context context) {
        if (WidgetRenderer.hasWidgets(context)) return;
        JobScheduler scheduler = (JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        scheduler.cancel(IMMEDIATE_JOB_ID);
        scheduler.cancel(PERIODIC_JOB_ID);
    }

    @Override public boolean onStartJob(JobParameters params) {
        if (activityVisible || !WidgetRenderer.hasWidgets(this) || !ACTIVE.compareAndSet(false, true)) return false;
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
                try { WidgetRenderer.showCached(this); }
                finally {
                    ACTIVE.set(false);
                    if (!stopped) jobFinished(params, false);
                }
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
