package com.cloudssd.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * Secrets (the PC pairing key, Google sign-ins) encrypted with a key that lives in Android's hardware-backed key
 * store and never leaves it. A copy of the app's data (a backup, a rooted file browser) holds only ciphertext.
 * When the key is gone (the phone was reset, or data was restored onto another phone) the value reads as "" and the
 * app simply asks to pair or sign in again.
 */
final class TokenStore {
    private static final String KEYSTORE = "AndroidKeyStore";
    private static final String ALIAS = "cloudssd_secrets";
    private static final String PREFIX = "v1:";

    private TokenStore() {
    }

    private static SecretKey key() throws Exception {
        KeyStore ks = KeyStore.getInstance(KEYSTORE);
        ks.load(null);
        if (ks.containsAlias(ALIAS)) return ((KeyStore.SecretKeyEntry) ks.getEntry(ALIAS, null)).getSecretKey();
        KeyGenerator gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE);
        // No user authentication: background jobs (camera backup, the Files app) must be able to use it.
        gen.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return gen.generateKey();
    }

    static String encrypt(String plain) {
        if (plain == null || plain.isEmpty()) return "";
        try {
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
            c.init(Cipher.ENCRYPT_MODE, key());
            byte[] iv = c.getIV();
            byte[] data = c.doFinal(plain.getBytes(StandardCharsets.UTF_8));
            return PREFIX + Base64.encodeToString(iv, Base64.NO_WRAP) + ":" + Base64.encodeToString(data, Base64.NO_WRAP);
        } catch (Exception e) {
            return "";
        }
    }

    static String decrypt(String stored) {
        if (stored == null || !stored.startsWith(PREFIX)) return "";
        try {
            String[] parts = stored.substring(PREFIX.length()).split(":", 2);
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
            c.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, Base64.decode(parts[0], Base64.NO_WRAP)));
            return new String(c.doFinal(Base64.decode(parts[1], Base64.NO_WRAP)), StandardCharsets.UTF_8);
        } catch (Exception e) {
            return ""; // key gone or data damaged: treated as "not paired / not signed in"
        }
    }

    /** Reads a secret; a value saved in plain text by an older version is encrypted on the way. */
    static String get(SharedPreferences p, String name) {
        String enc = p.getString(name + "_enc", null);
        if (enc != null) return decrypt(enc);
        String plain = p.getString(name, "");
        if (!plain.isEmpty()) {
            String sealed = encrypt(plain);
            if (!sealed.isEmpty()) p.edit().putString(name + "_enc", sealed).remove(name).commit();
        }
        return plain;
    }

    static void put(SharedPreferences p, String name, String value) {
        SharedPreferences.Editor e = p.edit().remove(name);
        if (value == null || value.isEmpty()) e.remove(name + "_enc");
        else {
            String sealed = encrypt(value);
            if (sealed.isEmpty()) e.putString(name, value); // no key store on this phone: better than losing the pairing
            else e.putString(name + "_enc", sealed);
        }
        e.commit();
    }

    /** The PC pairing key, as every part of the app (page, Files app, camera backup) uses it. */
    static String pcToken(Context c) {
        return get(c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE), "token");
    }

    static void setPcToken(Context c, String token) {
        put(c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE), "token", token);
    }
}
