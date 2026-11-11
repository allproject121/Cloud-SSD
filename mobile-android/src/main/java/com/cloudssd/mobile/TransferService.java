package com.cloudssd.mobile;

import android.app.Notification;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.IBinder;

/**
 * Keeps the app's process alive while files are moving, so closing the app does not stop a transfer. Android shows
 * its notification for as long as something is being sent or saved; it stops by itself when the queue is empty.
 */
public class TransferService extends Service {

    static void start(Context c) {
        Intent i = new Intent(c, TransferService.class);
        try {
            if (Build.VERSION.SDK_INT >= 26) c.startForegroundService(i);
            else c.startService(i);
        } catch (Exception ignored) {
            // Started from the background where Android does not allow it: the transfer still runs while the app lives.
        }
    }

    static void stopIfIdle(Context c) {
        if (TransferHub.get(c).busy()) return;
        try {
            c.stopService(new Intent(c, TransferService.class));
        } catch (Exception ignored) {
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        Notification n = TransferHub.get(this).builder("Cloud-SSD", "Moving files between this phone and the PC…",
                android.R.drawable.stat_sys_upload).setOngoing(true).build();
        try {
            if (Build.VERSION.SDK_INT >= 29) startForeground(TransferHub.SUMMARY_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);
            else startForeground(TransferHub.SUMMARY_ID, n);
        } catch (Exception e) {
            stopSelf();
            return START_NOT_STICKY;
        }
        if (!TransferHub.get(this).busy()) stopSelf();
        return START_NOT_STICKY;
    }

    /** Android 15 allows a data-sync service six hours a day; after that every transfer is paused and can be retried. */
    @Override
    public void onTimeout(int startId, int fgsType) {
        TransferHub.get(this).pauseAll("Paused by Android (background time used up today) — tap Retry");
        stopSelf();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}
