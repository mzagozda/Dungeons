package com.godot.game;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.util.Log;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.UUID;
import java.util.concurrent.Executors;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.atomic.AtomicInteger;

/** Copies granted content into a bounded private inbox; C# validates before promoting to levels. */
public final class LevelIntentInbox {
    private static final ExecutorService WORKER = Executors.newSingleThreadExecutor();
    private static final long MAX_BYTES = 64L * 1024 * 1024;
    private static final AtomicInteger PENDING = new AtomicInteger();
    private LevelIntentInbox() {}
    public static int sdkInt() { return android.os.Build.VERSION.SDK_INT; }
    public static boolean isBusy() { return PENDING.get() > 0; }
    public static void enqueue(Activity activity, Intent intent) {
        if (intent == null || !Intent.ACTION_VIEW.equals(intent.getAction())) return;
        Uri uri = intent.getData();
        if (uri == null || !"content".equals(uri.getScheme())) return;
        // A renderer relaunch must not enqueue the same source again.
        intent.setData(null); intent.setAction(Intent.ACTION_MAIN);
        PENDING.incrementAndGet();
        WORKER.execute(() -> {
            File directory = new File(activity.getFilesDir(), "incoming-levels");
            File temporary = null;
            String name = "incoming-" + UUID.randomUUID().toString().replace("-", "");
            try {
                if (!directory.isDirectory() && !directory.mkdirs()) throw new java.io.IOException("Inbox unavailable");
                String[] pending = directory.list();
                if (pending == null || pending.length >= 8) throw new java.io.IOException("Inbox full");
                temporary = new File(directory, name + ".tmp");
                try (InputStream input = activity.getContentResolver().openInputStream(uri);
                     FileOutputStream output = new FileOutputStream(temporary)) {
                    if (input == null) throw new java.io.IOException("Unreadable source");
                    byte[] buffer = new byte[8192]; long total = 0; int read;
                    while ((read = input.read(buffer)) != -1) {
                        total += read; if (total > MAX_BYTES) throw new java.io.IOException("Package exceeds limit");
                        output.write(buffer, 0, read);
                    }
                    output.getFD().sync();
                }
                if (!temporary.renameTo(new File(directory, name + ".dlvl"))) throw new java.io.IOException("Inbox commit failed");
                temporary = null; Log.i("BlockDungeonsImport", "IMPORT_QUEUED");
            } catch (Exception error) {
                Log.w("BlockDungeonsImport", "IMPORT_COPY_FAILED: " + error.getClass().getSimpleName());
                try (FileOutputStream status = new FileOutputStream(new File(directory, "copy.error"))) { status.write(1); }
                catch (Exception ignored) { /* The log retains the failure if private storage is unavailable. */ }
            } finally { if (temporary != null) temporary.delete(); PENDING.decrementAndGet(); }
        });
    }
}
