package com.universalgacha.recovery;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;

import com.unity3d.player.UnityPlayer;

import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

public final class RecoveryDocumentBridge {
    private static final int REQUEST_CREATE = 41171;
    private static final int REQUEST_OPEN = 41172;
    private static final long MAXIMUM_BYTES = 16L * 1024L * 1024L;

    private RecoveryDocumentBridge() { }

    public static void createDocument(
            final String callbackObject,
            final String callbackMethod,
            final String requestId,
            final String suggestedFileName,
            final String sourcePath) {
        launch(callbackObject, callbackMethod, requestId, suggestedFileName, sourcePath, true);
    }

    public static void openDocument(
            final String callbackObject,
            final String callbackMethod,
            final String requestId,
            final String destinationPath) {
        launch(callbackObject, callbackMethod, requestId, null, destinationPath, false);
    }

    private static void launch(
            final String callbackObject,
            final String callbackMethod,
            final String requestId,
            final String suggestedFileName,
            final String localPath,
            final boolean exporting) {
        new Handler(Looper.getMainLooper()).post(() -> {
            Activity activity = UnityPlayer.currentActivity;
            if (activity == null) {
                send(callbackObject, callbackMethod, requestId, false, null, "Android activity is unavailable.");
                return;
            }
            String tag = "universal-gacha-recovery-picker";
            if (activity.getFragmentManager().findFragmentByTag(tag) != null) {
                send(callbackObject, callbackMethod, requestId, false, null, "A document picker is already open.");
                return;
            }
            PickerFragment fragment = PickerFragment.create(
                    callbackObject,
                    callbackMethod,
                    requestId,
                    suggestedFileName,
                    localPath,
                    exporting);
            activity.getFragmentManager().beginTransaction()
                    .add(fragment, tag)
                    .commitAllowingStateLoss();
        });
    }

    public static final class PickerFragment extends Fragment {
        private String callbackObject;
        private String callbackMethod;
        private String requestId;
        private String suggestedFileName;
        private String localPath;
        private boolean exporting;
        private boolean launched;

        static PickerFragment create(
                String callbackObject,
                String callbackMethod,
                String requestId,
                String suggestedFileName,
                String localPath,
                boolean exporting) {
            PickerFragment fragment = new PickerFragment();
            Bundle arguments = new Bundle();
            arguments.putString("callbackObject", callbackObject);
            arguments.putString("callbackMethod", callbackMethod);
            arguments.putString("requestId", requestId);
            arguments.putString("suggestedFileName", suggestedFileName);
            arguments.putString("localPath", localPath);
            arguments.putBoolean("exporting", exporting);
            fragment.setArguments(arguments);
            return fragment;
        }

        @Override
        public void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            Bundle arguments = getArguments();
            callbackObject = arguments.getString("callbackObject");
            callbackMethod = arguments.getString("callbackMethod");
            requestId = arguments.getString("requestId");
            suggestedFileName = arguments.getString("suggestedFileName");
            localPath = arguments.getString("localPath");
            exporting = arguments.getBoolean("exporting");
            launched = savedInstanceState != null && savedInstanceState.getBoolean("launched", false);
        }

        @Override
        public void onSaveInstanceState(Bundle outState) {
            outState.putBoolean("launched", launched);
            super.onSaveInstanceState(outState);
        }

        @Override
        public void onResume() {
            super.onResume();
            if (launched) return;
            launched = true;
            Intent intent = new Intent(exporting
                    ? Intent.ACTION_CREATE_DOCUMENT
                    : Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("application/octet-stream");
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION |
                    Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            if (exporting) {
                intent.putExtra(Intent.EXTRA_TITLE, suggestedFileName);
                startActivityForResult(intent, REQUEST_CREATE);
            } else {
                intent.putExtra(Intent.EXTRA_MIME_TYPES,
                        new String[] { "application/json", "application/octet-stream" });
                startActivityForResult(intent, REQUEST_OPEN);
            }
        }

        @Override
        public void onActivityResult(int requestCode, int resultCode, Intent data) {
            super.onActivityResult(requestCode, resultCode, data);
            if (requestCode != REQUEST_CREATE && requestCode != REQUEST_OPEN) return;
            if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null) {
                finish(false, null, "cancelled");
                return;
            }

            Uri uri = data.getData();
            try {
                if (exporting) {
                    copyFileToUri(new File(localPath), uri);
                    finish(true, uri.toString(), null);
                } else {
                    copyUriToFile(uri, new File(localPath));
                    finish(true, localPath, null);
                }
            } catch (Exception exception) {
                finish(false, null, exception.getMessage());
            }
        }

        private void copyFileToUri(File source, Uri destination) throws Exception {
            if (!source.isFile() || source.length() <= 0 || source.length() > MAXIMUM_BYTES)
                throw new IllegalArgumentException("The staged recovery file is invalid.");
            try (InputStream input = new FileInputStream(source);
                 OutputStream output = getActivity().getContentResolver().openOutputStream(destination, "w")) {
                if (output == null) throw new IllegalStateException("The selected document cannot be written.");
                copy(input, output);
            }
        }

        private void copyUriToFile(Uri source, File destination) throws Exception {
            File parent = destination.getParentFile();
            if (parent != null && !parent.exists() && !parent.mkdirs())
                throw new IllegalStateException("The recovery staging directory cannot be created.");
            try (InputStream input = getActivity().getContentResolver().openInputStream(source);
                 OutputStream output = new FileOutputStream(destination, false)) {
                if (input == null) throw new IllegalStateException("The selected document cannot be read.");
                copy(input, output);
            } catch (Exception exception) {
                if (destination.exists()) destination.delete();
                throw exception;
            }
        }

        private static void copy(InputStream input, OutputStream output) throws Exception {
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = input.read(buffer)) >= 0) {
                total += read;
                if (total > MAXIMUM_BYTES)
                    throw new IllegalArgumentException("The selected document exceeds 16 MiB.");
                output.write(buffer, 0, read);
            }
            output.flush();
        }

        private void finish(boolean succeeded, String path, String error) {
            send(callbackObject, callbackMethod, requestId, succeeded, path, error);
            if (getActivity() != null) {
                getActivity().getFragmentManager().beginTransaction()
                        .remove(this)
                        .commitAllowingStateLoss();
            }
        }
    }

    private static void send(
            String callbackObject,
            String callbackMethod,
            String requestId,
            boolean succeeded,
            String path,
            String error) {
        try {
            JSONObject result = new JSONObject();
            result.put("requestId", requestId);
            result.put("succeeded", succeeded);
            result.put("path", path == null ? JSONObject.NULL : path);
            result.put("error", error == null ? JSONObject.NULL : error);
            UnityPlayer.UnitySendMessage(callbackObject, callbackMethod, result.toString());
        } catch (Exception exception) {
            UnityPlayer.UnitySendMessage(
                    callbackObject,
                    callbackMethod,
                    "{\"requestId\":" + JSONObject.quote(requestId) +
                            ",\"succeeded\":false,\"error\":\"Document picker callback failed.\"}");
        }
    }
}
