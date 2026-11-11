package com.cloudssd.mobile;

import android.app.job.JobParameters;
import android.app.job.JobService;

import java.util.concurrent.atomic.AtomicBoolean;

/** Runs {@link CameraBackup} when Android says it is time: every few hours, after a new photo, or on "Back up now". */
public class CameraBackupService extends JobService {
    private final AtomicBoolean stop = new AtomicBoolean();

    @Override
    public boolean onStartJob(final JobParameters params) {
        stop.set(false);
        new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    if (CameraBackup.enabled(CameraBackupService.this) || params.getJobId() == CameraBackup.JOB_NOW)
                        CameraBackup.run(CameraBackupService.this, stop);
                } finally {
                    // The "new photo" watch only fires once, so it is set again after every run.
                    CameraBackup.watchForNewMedia(CameraBackupService.this);
                    jobFinished(params, false);
                    CameraBackup.continueIfMore(CameraBackupService.this);
                }
            }
        }, "cloudssd-backup").start();
        return true;
    }

    /** Android wants the time back (ten minutes are over, or Wi-Fi dropped): stop after the current block. */
    @Override
    public boolean onStopJob(JobParameters params) {
        stop.set(true);
        return false; // the next scheduled run continues where this one stopped
    }
}
