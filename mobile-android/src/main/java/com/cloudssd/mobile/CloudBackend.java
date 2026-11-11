package com.cloudssd.mobile;

import java.io.IOException;
import java.io.InputStream;
import java.util.List;

/**
 * Where the cloud files really are: the Cloud-SSD app on the PC ({@link PcBackend}), or Google Drive directly when
 * the PC is off ({@link DriveBackend}); {@link Backends#files} picks the one that answers.
 *
 * An item is named by an id of the form "&lt;drive&gt;:&lt;folder/file&gt;", for example "cloudssd:Photos/a.jpg";
 * "&lt;drive&gt;:" is the drive itself.
 */
interface CloudBackend {

    final class RootInfo {
        String id;
        String title;
        boolean readOnly;
        /** Whether items can be added directly at the top of this drive. */
        boolean canCreate;
        long totalBytes = -1;
        long freeBytes = -1;
    }

    final class DocInfo {
        String id;
        String name;
        boolean dir;
        long size;
        /** Milliseconds since 1970, 0 when unknown. */
        long modified;
        /** May be renamed, moved, deleted (and, for a file, overwritten). */
        boolean canEdit;
        /** A folder that accepts new items. */
        boolean canCreate;
        /** A Google Docs/Sheets/Slides file, read as its Word/Excel/PowerPoint export (read-only). */
        boolean exported;
    }

    /** A failed request. {@link #status} is the HTTP-style code, or {@link #UNREACHABLE} when the backend did not answer. */
    final class BackendException extends IOException {
        static final int UNREACHABLE = -1;
        static final int NOT_PAIRED = -2;
        final int status;

        BackendException(int status, String message) {
            super(message);
            this.status = status;
        }
    }

    /** True when there is something to talk to (the phone is paired). Never touches the network. */
    boolean isConfigured();

    List<RootInfo> roots() throws IOException;

    List<DocInfo> list(String folderId) throws IOException;

    DocInfo stat(String id) throws IOException;

    /** Creates an empty file or a folder. If the name is taken a free one is chosen; the result tells which. */
    DocInfo create(String parentId, String name, boolean dir) throws IOException;

    DocInfo rename(String id, String name) throws IOException;

    DocInfo move(String id, String toFolderId) throws IOException;

    void delete(String id) throws IOException;

    /** The file's bytes from {@code offset} to its end. Close it to stop the transfer. */
    InputStream openRead(String id, long offset) throws IOException;

    /**
     * Replaces the file's content with everything {@code data} delivers. Nothing is saved unless the stream ends
     * normally: if reading it throws, the upload is abandoned and the old content stays.
     */
    DocInfo write(String id, InputStream data) throws IOException;

    /** A small JPEG of a photo or video (about {@code px} pixels), or null when there is none. */
    InputStream thumbnail(String id, int px) throws IOException;

    /** Items below a folder whose name contains every word of {@code query}. */
    List<DocInfo> search(String folderId, String query, int max) throws IOException;

    /** "PC" or "Google Drive": what the user is told the files come from. */
    String label();
}
