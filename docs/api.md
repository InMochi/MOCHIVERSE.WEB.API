# MOCHI.VERSE.API - Web Frontend Integration Guide

Dokumentasi kontrak REST API untuk frontend web Mochiverse (`MOCHI.VERSE.API`).

## 1. Base URL, HTTPS & Dev Proxy

| Environment | Base URL | Swagger UI |
| --- | --- | --- |
| Development (HTTPS, wajib untuk `__Host-` cookie) | `https://localhost:7003` | `https://localhost:7003/swagger` |
| Development (HTTP, tidak bisa set cookie `__Host-` / `Secure`) | `http://localhost:5176` | - |

- **Wajib HTTPS di Development:** Cookie sesi `__Host-mochi_access`, cookie CSRF internal `__Host-mochi_xsrf`, dan cookie `XSRF-TOKEN` memakai `Secure = true` dan `SameSite = Lax`.
- **Rekomendasi Dev Frontend:** Gunakan dev proxy same-origin di Vite/Next.js yang meneruskan `/api/*` ke `https://localhost:7003` (atau jalankan frontend juga di HTTPS), sehingga browser tidak mengalami kendala `http://` vs `https://` saat membaca/mengirim cookie `Secure` + `SameSite=Lax`.
- **CORS:** Policy CORS mengizinkan origin eksplisit dari `Cors:AllowedOrigins` (default Dev: `http://localhost:3000`, `http://localhost:5173`) dengan `AllowCredentials()`, method `GET/POST/PUT/DELETE/OPTIONS`, dan header `Authorization`, `Content-Type`, `X-XSRF-TOKEN`.

---

## 2. Autentikasi Cookie & Proteksi CSRF

Sesi web **tidak menggunakan Bearer token di JSON body / `localStorage`**, melainkan **HttpOnly Cookie + CSRF Double-Submit**.

- **Cookie Sesi:** `__Host-mochi_access` (`HttpOnly = true`, `Secure = true`, `SameSite = Lax`, `Path = "/"`). Dikirim otomatis oleh browser saat `credentials: "include"`.
- **Cookie & Header CSRF:**
  - Endpoint inisialisasi: `GET /api/auth/csrf`
  - Cookie terbaca JS: `XSRF-TOKEN` (`HttpOnly = false`, `Secure = true`, `SameSite = Lax`, `Path = "/"`) + cookie internal `__Host-mochi_xsrf` (`HttpOnly = true`)
  - Header wajib pada seluruh request `POST`, `PUT`, `DELETE`, `PATCH`: `X-XSRF-TOKEN`
- **Aturan Siklus CSRF (`IAntiforgery`):**
  1. Panggil `GET /api/auth/csrf` saat app dimuat (atau sebelum mutasi pertama) dengan `credentials: "include"`.
  2. Simpan `data.requestToken` (atau baca cookie `XSRF-TOKEN`) lalu kirim di header `X-XSRF-TOKEN` pada setiap `POST`/`PUT`/`DELETE`.
  3. **Wajib panggil `GET /api/auth/csrf` ulang setelah `login`, `register`, dan `logout`**, karena `IAntiforgery` mengikat token CSRF ke identity user (anonymous vs authenticated). Token sebelum login akan ditolak `403` setelah sesi login terbentuk.

---

## 3. Format Response, Pagination & Error

### Wrapper Standar (`APIResponse`)

```json
{
  "ok": true,
  "message": "Success",
  "data": {}
}
```

### Wrapper Pagination Seragam

Semua endpoint list (`titles/search`, `titles/{source}/{id}/ratings`, `me/watchlist`) memakai struktur:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalItems": 45,
  "totalPages": 3,
  "hasNextPage": true
}
```

### Error Format Seragam

- **`400 Bad Request` (Validasi DTO DataAnnotations):**
  ```json
  {
    "ok": false,
    "message": "Validation failed.",
    "data": null,
    "errors": {
      "username": ["The Username field is required."]
    }
  }
  ```
- **`401 Unauthorized`** (cookie sesi tidak ada, expired, atau sudah di-revoke saat logout):
  ```json
  {
    "ok": false,
    "message": "Authorization denied. Login again to renew session.",
    "data": null
  }
  ```
- **`403 Forbidden`** (token/cookie `X-XSRF-TOKEN` tidak ada atau tidak valid):
  ```json
  {
    "ok": false,
    "message": "Invalid or missing CSRF token.",
    "data": null
  }
  ```
- **`429 Too Many Requests`** (menyertakan header `Retry-After` jika tersedia):
  ```json
  {
    "ok": false,
    "message": "Too many requests. Please try again later.",
    "data": null
  }
  ```
  Batas rate limit:
  - `POST /api/auth/register`, `POST /api/auth/login`: `10` req/menit per IP
  - `GET /api/titles/search`, `GET /api/titles/{source}/{id}`: `30` req/menit per IP
  - `PUT`/`DELETE` rating & watchlist: `30` req/menit per user
- **`500` / `502` / `503`:** Selalu memakai `{ "ok": false, "message": "...", "data": null }` tanpa stack trace atau detail error SQL Server.

---

## 4. Auth Endpoints

### `GET /api/auth/csrf`
- **Auth:** Public (`Cache-Control: no-store`)
- **Response `200 OK`:** Set cookie `XSRF-TOKEN` dan `__Host-mochi_xsrf`, lalu mengembalikan:
  ```json
  {
    "ok": true,
    "message": null,
    "data": {
      "headerName": "X-XSRF-TOKEN",
      "requestToken": "..."
    }
  }
  ```

### `POST /api/auth/register`
- **Auth:** Public + CSRF (`X-XSRF-TOKEN`)
- **Rate Limit:** 10 req/menit per IP
- **Request Body:**
  ```json
  {
    "username": "mochi_user",
    "email": "user@example.com",
    "password": "strongpassword123"
  }
  ```
  - `username`: wajib, 3-50 karakter, hanya huruf, angka, `.`, `_`, `-`
  - `email`: wajib, format email valid, maks 255 karakter
  - `password`: wajib, 8-128 karakter
- **Response `201 Created`:** Set cookie `__Host-mochi_access`, mengembalikan tanpa `accessToken`:
  ```json
  {
    "ok": true,
    "message": "User registered.",
    "data": {
      "expiresAt": "2026-11-22T01:00:00Z",
      "user": {
        "id": 1,
        "username": "mochi_user",
        "email": "user@example.com",
        "role": "User",
        "createdAt": "2026-11-22T00:00:00Z"
      }
    }
  }
  ```
- **Error:** `400` validation failed, `403` CSRF invalid, `409 Conflict` jika `username` atau `email` sudah terdaftar.

### `POST /api/auth/login`
- **Auth:** Public + CSRF (`X-XSRF-TOKEN`)
- **Rate Limit:** 10 req/menit per IP
- **Request Body:**
  ```json
  {
    "identifier": "mochi_user",
    "password": "strongpassword123"
  }
  ```
  - `identifier`: bisa diisi `username` atau `email`.
- **Response `200 OK`:** Set cookie `__Host-mochi_access`, mengembalikan `data.expiresAt` dan `data.user` (tanpa `accessToken`).
- **Error:** `401 Unauthorized` (`"Invalid credentials."`), `403` CSRF invalid.

### `POST /api/auth/logout`
- **Auth:** Cookie `__Host-mochi_access` + CSRF (`X-XSRF-TOKEN`)
- **Request Body:** Tidak ada.
- **Response `200 OK`:** Menghapus cookie `__Host-mochi_access` dan mencatat `jti` ke `dbo.RevokedTokens`:
  ```json
  {
    "ok": true,
    "message": "Logged out successfully.",
    "data": null
  }
  ```

### `GET /api/auth/me`
- **Auth:** Cookie `__Host-mochi_access` (`Cache-Control: no-store`)
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": null,
    "data": {
      "id": 1,
      "username": "mochi_user",
      "email": "user@example.com",
      "role": "User",
      "createdAt": "2026-11-22T00:00:00Z"
    }
  }
  ```

---

## 5. Titles Endpoints

Identitas setiap judul menggunakan pasangan `(source, id)`:
- `source`: `"tmdb"` (film) atau `"anilist"` (anime)
- `id`: integer positif ID eksternal (misal `603` untuk TMDB atau `1535` untuk AniList)

### `GET /api/titles/search`
- **Auth:** Public
- **Rate Limit:** 30 req/menit per IP
- **Query Params:**
  - `q` (wajib, maks 100 karakter): kata kunci pencarian
  - `type` (opsional): `"movie"` | `"anime"` (jika kosong, mencari ke kedua provider)
  - `page` (opsional, default `1`, rentang `1..100`)
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": null,
    "data": {
      "items": [
        {
          "source": "tmdb",
          "externalId": "603",
          "mediaType": "movie",
          "title": "The Matrix",
          "posterUrl": "https://image.tmdb.org/t/p/w500/...",
          "releaseYear": 1999,
          "synopsis": "..."
        }
      ],
      "page": 1,
      "pageSize": 20,
      "totalItems": 42,
      "totalPages": 3,
      "hasNextPage": true
    }
  }
  ```

### `GET /api/titles/{source}/{id}`
- **Auth:** Public (jika cookie `__Host-mochi_access` valid ikut terkirim, mengembalikan `myRating` & `myWatchlist` serta `Cache-Control: no-store`)
- **Rate Limit:** 30 req/menit per IP
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": null,
    "data": {
      "source": "tmdb",
      "externalId": "603",
      "mediaType": "movie",
      "title": "The Matrix",
      "posterUrl": "https://image.tmdb.org/t/p/w500/...",
      "releaseYear": 1999,
      "synopsis": "...",
      "genres": ["Action", "Science Fiction"],
      "cachedAt": "2026-11-22T00:00:00Z",
      "totalVotes": 12,
      "averageScore": 8.75,
      "myRating": {
        "id": 10,
        "score": 9,
        "review": "Classic sci-fi.",
        "createdAt": "2026-11-22T00:01:00Z",
        "updatedAt": null
      },
      "myWatchlist": {
        "id": 5,
        "status": "Completed",
        "createdAt": "2026-11-22T00:02:00Z",
        "updatedAt": null
      }
    }
  }
  ```
  - Jika user belum login atau belum punya rating/watchlist untuk judul ini, `myRating` dan/atau `myWatchlist` bernilai `null`.
  - Jika poster tidak tersedia, `posterUrl` selalu `null` (bukan string kosong).

---

## 6. Ratings Endpoints

### `GET /api/titles/{source}/{id}/ratings`
- **Auth:** Public
- **Query Params:**
  - `page` (opsional, default `1`, minimal `1`)
  - `pageSize` (opsional, default `20`, rentang `1..50`)
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": null,
    "data": {
      "source": "tmdb",
      "externalId": "603",
      "totalVotes": 1,
      "averageScore": 9.0,
      "items": [
        {
          "id": 10,
          "username": "mochi_user",
          "score": 9,
          "review": "Classic sci-fi.",
          "createdAt": "2026-11-22T00:01:00Z",
          "updatedAt": null
        }
      ],
      "page": 1,
      "pageSize": 20,
      "totalItems": 1,
      "totalPages": 1,
      "hasNextPage": false
    }
  }
  ```

### `PUT /api/titles/{source}/{id}/rating`
- **Auth:** Cookie `__Host-mochi_access` + CSRF (`X-XSRF-TOKEN`)
- **Rate Limit:** 30 req/menit per user
- **Request Body:**
  ```json
  {
    "score": 9,
    "review": "Classic sci-fi."
  }
  ```
- **Response `200 OK`:** Mengembalikan `RatingRecord` (`id`, `userId`, `username`, `source`, `externalId`, `score`, `review`, `createdAt`, `updatedAt`).

### `DELETE /api/titles/{source}/{id}/rating`
- **Auth:** Cookie `__Host-mochi_access` + CSRF (`X-XSRF-TOKEN`)
- **Rate Limit:** 30 req/menit per user
- **Response `200 OK`:** `{ "ok": true, "message": "Rating deleted.", "data": null }`

---

## 7. Watchlist Endpoints

Nilai `status` yang valid: `"PlanToWatch"`, `"Watching"`, `"Completed"`.

### `GET /api/me/watchlist`
- **Auth:** Cookie `__Host-mochi_access` (`Cache-Control: no-store`)
- **Query Params:**
  - `status` (opsional): `"PlanToWatch"` | `"Watching"` | `"Completed"`
  - `page` (opsional, default `1`, minimal `1`)
  - `pageSize` (opsional, default `20`, rentang `1..50`)
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": null,
    "data": {
      "items": [
        {
          "id": 5,
          "source": "anilist",
          "externalId": "1535",
          "status": "Watching",
          "mediaType": "anime",
          "title": "Death Note",
          "posterUrl": "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx1535-kUgkcrfOrkUM.jpg",
          "releaseYear": 2006,
          "createdAt": "2026-11-22T00:02:00Z",
          "updatedAt": null
        }
      ],
      "page": 1,
      "pageSize": 20,
      "totalItems": 1,
      "totalPages": 1,
      "hasNextPage": false,
      "counts": {
        "planToWatch": 4,
        "watching": 1,
        "completed": 10
      }
    }
  }
  ```

### `PUT /api/me/watchlist/{source}/{id}`
- **Auth:** Cookie `__Host-mochi_access` + CSRF (`X-XSRF-TOKEN`)
- **Rate Limit:** 30 req/menit per user
- **Request Body:** `{ "status": "Watching" }`
- **Response `200 OK`:** `{ "ok": true, "message": "Watchlist updated.", "data": WatchlistItem }`

### `DELETE /api/me/watchlist/{source}/{id}`
- **Auth:** Cookie `__Host-mochi_access` + CSRF (`X-XSRF-TOKEN`)
- **Rate Limit:** 30 req/menit per user
- **Response `200 OK`:** `{ "ok": true, "message": "Watchlist entry removed.", "data": null }`

---

## 8. Contoh Fetch Wrapper (Frontend TypeScript)

```ts
const API_BASE = ""; // Gunakan "" jika memakai dev proxy same-origin, atau "https://localhost:7003"

export interface ApiResponse<T> {
  ok: boolean;
  message: string | null;
  data: T | null;
  errors?: Record<string, string[]>;
}

let csrfTokenCache: string | null = null;

export async function refreshCsrfToken(): Promise<string> {
  const res = await fetch(`${API_BASE}/api/auth/csrf`, {
    method: "GET",
    credentials: "include",
  });
  const body = (await res.json()) as ApiResponse<{ headerName: string; requestToken: string }>;
  if (!res.ok || !body.data?.requestToken) {
    throw new Error(body.message ?? "Failed to initialize CSRF token.");
  }
  csrfTokenCache = body.data.requestToken;
  return csrfTokenCache;
}

export async function apiFetch<T>(
  path: string,
  options: RequestInit = {}
): Promise<ApiResponse<T>> {
  const method = (options.method ?? "GET").toUpperCase();
  const isStateChanging = !["GET", "HEAD", "OPTIONS", "TRACE"].includes(method);

  const headers = new Headers(options.headers);
  if (options.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  if (isStateChanging) {
    const csrf = csrfTokenCache ?? (await refreshCsrfToken());
    headers.set("X-XSRF-TOKEN", csrf);
  }

  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    method,
    headers,
    credentials: "include",
  });

  const body = (await res.json()) as ApiResponse<T>;
  if (!res.ok) {
    throw new Error(body.message ?? `Request failed (${res.status})`);
  }

  return body;
}
```
