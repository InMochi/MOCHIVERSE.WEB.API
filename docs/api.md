# MOCHI.VERSE.API - Web Frontend Integration Guide

Dokumentasi kontrak REST API untuk frontend web Mochiverse (`MOCHI.VERSE.API`).

## 1. Base URL & Environment

| Environment | Base URL | Swagger UI |
| --- | --- | --- |
| Development (HTTPS) | `https://localhost:7003` | `https://localhost:7003/swagger` |
| Development (HTTP) | `http://localhost:5176` | `http://localhost:5176/swagger` |

> **Catatan CORS:** API mengizinkan origin dari `Cors:AllowedOrigins` (default Development di `appsettings.Development.json`: `http://localhost:3000` dan `http://localhost:5173`, method `GET/POST/PUT/DELETE/OPTIONS`, header `Authorization` & `Content-Type`). Tambahkan origin lain di `Cors:AllowedOrigins` jika frontend berjalan di port berbeda.

---

## 2. Format Response & Auth Header

Semua endpoint kustom mengembalikan JSON camelCase dengan struktur wrapper `APIResponse`:

```json
{
  "ok": true,
  "message": "Success",
  "data": {}
}
```

- **Auth Header:** Endpoint bertanda `Auth: Bearer` wajib mengirim header:
  ```http
  Authorization: Bearer <accessToken>
  ```
- **Error `401 Unauthorized`** (token tidak ada, expired, atau sudah logout/revoked):
  ```json
  {
    "ok": false,
    "message": "Authorization denied. Login again to renew session.",
    "data": null
  }
  ```
- **Error `400 Bad Request` dari DataAnnotations:** Model validation otomatis ASP.NET Core mengembalikan `ValidationProblemDetails` (field `errors` per properti), sedangkan validasi bisnis/parameter mengembalikan wrapper `{ "ok": false, "message": "...", "data": null }`.
- **Rate Limit:** Endpoint `POST /api/auth/register` dan `POST /api/auth/login` dibatasi `10` request/menit per IP (`429 Too Many Requests`).

---

## 3. Auth Endpoints

### `POST /api/auth/register`
- **Auth:** Public
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
- **Response `201 Created`:**
  ```json
  {
    "ok": true,
    "message": "User registered.",
    "data": {
      "accessToken": "eyJhbGciOiJIUzI1NiIs...",
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
- **Error:** `409 Conflict` jika `username` atau `email` sudah terdaftar.

### `POST /api/auth/login`
- **Auth:** Public
- **Rate Limit:** 10 req/menit per IP
- **Request Body:**
  ```json
  {
    "identifier": "mochi_user",
    "password": "strongpassword123"
  }
  ```
  - `identifier`: bisa diisi `username` atau `email`.
- **Response `200 OK`:** Sama seperti `register` (`data.accessToken`, `data.expiresAt`, `data.user`).
- **Error:** `401 Unauthorized` (`"Invalid credentials."`).

### `POST /api/auth/logout`
- **Auth:** Bearer
- **Request Body:** Tidak ada.
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": "Logged out successfully.",
    "data": null
  }
  ```
- **Perilaku:** Token (`jti`) langsung dicatat ke `dbo.RevokedTokens` dan cache memori sehingga token lama langsung ditolak `401` pada request berikutnya. Frontend tetap wajib menghapus token dari state/storage.

### `GET /api/auth/me`
- **Auth:** Bearer
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

## 4. Titles Endpoints

Identitas setiap judul menggunakan pasangan `(source, id)`:
- `source`: `"tmdb"` (film) atau `"jikan"` (anime)
- `id`: integer positif ID eksternal (misal `603` untuk TMDB atau `1535` untuk Jikan)

### `GET /api/titles/search`
- **Auth:** Public
- **Query Params:**
  - `q` (wajib, maks 100 karakter): kata kunci pencarian
  - `type` (opsional): `"movie"` | `"anime"` (jika kosong, mencari ke kedua provider)
  - `page` (opsional, default `1`, rentang `1..100`)
- **Contoh:** `GET /api/titles/search?q=matrix&type=movie&page=1`
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": null,
    "data": [
      {
        "source": "tmdb",
        "externalId": "603",
        "mediaType": "movie",
        "title": "The Matrix",
        "posterUrl": "https://image.tmdb.org/t/p/w500/...",
        "releaseYear": 1999,
        "synopsis": "..."
      }
    ]
  }
  ```

### `GET /api/titles/{source}/{id}`
- **Auth:** Public
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
      "cachedAt": "2026-11-22T00:00:00Z",
      "totalVotes": 12,
      "averageScore": 8.75
    }
  }
  ```
- **Error:** `400 Bad Request` (source/id tidak valid), `404 Not Found`, `502 Bad Gateway` (API eksternal gagal), `503 Service Unavailable` (token TMDB belum dikonfigurasi).

---

## 5. Ratings Endpoints

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
      "page": 1,
      "pageSize": 20,
      "items": [
        {
          "id": 10,
          "username": "mochi_user",
          "score": 9,
          "review": "Classic sci-fi.",
          "createdAt": "2026-11-22T00:01:00Z",
          "updatedAt": null
        }
      ]
    }
  }
  ```

### `PUT /api/titles/{source}/{id}/rating`
- **Auth:** Bearer
- **Request Body:**
  ```json
  {
    "score": 9,
    "review": "Classic sci-fi."
  }
  ```
  - `score`: wajib, integer `1..10`
  - `review`: opsional, maks 2000 karakter
- **Perilaku:** Idempotent (insert jika belum ada, update jika user sudah pernah memberi rating pada judul ini). Backend otomatis memverifikasi dan menyimpan metadata judul ke `TitleCache`.
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": "Rating saved.",
    "data": {
      "id": 10,
      "userId": 1,
      "username": "mochi_user",
      "source": "tmdb",
      "externalId": "603",
      "score": 9,
      "review": "Classic sci-fi.",
      "createdAt": "2026-11-22T00:01:00Z",
      "updatedAt": null
    }
  }
  ```

### `DELETE /api/titles/{source}/{id}/rating`
- **Auth:** Bearer
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": "Rating deleted.",
    "data": null
  }
  ```
- **Error:** `404 Not Found` jika rating milik user untuk judul tersebut belum ada.

---

## 6. Watchlist Endpoints

Nilai `status` yang valid: `"PlanToWatch"`, `"Watching"`, `"Completed"`.

### `GET /api/me/watchlist`
- **Auth:** Bearer
- **Query Params:**
  - `status` (opsional): `"PlanToWatch"` | `"Watching"` | `"Completed"`
  - `page` (opsional, default `1`, minimal `1`)
  - `pageSize` (opsional, default `20`, rentang `1..50`)
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": null,
    "data": [
      {
        "id": 5,
        "source": "jikan",
        "externalId": "1535",
        "status": "Watching",
        "mediaType": "anime",
        "title": "Death Note",
        "posterUrl": "https://cdn.myanimelist.net/images/anime/1079/138100.jpg",
        "releaseYear": 2006,
        "createdAt": "2026-11-22T00:02:00Z",
        "updatedAt": null
      }
    ]
  }
  ```

### `PUT /api/me/watchlist/{source}/{id}`
- **Auth:** Bearer
- **Request Body:**
  ```json
  {
    "status": "Watching"
  }
  ```
- **Perilaku:** Idempotent (tambah ke watchlist atau ubah status). Backend otomatis mengisi `TitleCache` jika judul belum ada di cache.
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": "Watchlist updated.",
    "data": {
      "id": 5,
      "source": "jikan",
      "externalId": "1535",
      "status": "Watching",
      "mediaType": "anime",
      "title": "Death Note",
      "posterUrl": "https://cdn.myanimelist.net/images/anime/1079/138100.jpg",
      "releaseYear": 2006,
      "createdAt": "2026-11-22T00:02:00Z",
      "updatedAt": "2026-11-22T00:03:00Z"
    }
  }
  ```

### `DELETE /api/me/watchlist/{source}/{id}`
- **Auth:** Bearer
- **Response `200 OK`:**
  ```json
  {
    "ok": true,
    "message": "Watchlist entry removed.",
    "data": null
  }
  ```
- **Error:** `404 Not Found` jika entri watchlist tidak ditemukan.

---

## 7. Contoh Fetch Wrapper (Frontend TypeScript)

```ts
const API_BASE = "https://localhost:7003";

export interface ApiResponse<T> {
  ok: boolean;
  message: string | null;
  data: T | null;
}

export async function apiFetch<T>(
  path: string,
  options: RequestInit = {},
  token?: string
): Promise<ApiResponse<T>> {
  const headers = new Headers(options.headers);
  if (options.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const res = await fetch(`${API_BASE}${path}`, { ...options, headers });
  const body = (await res.json()) as ApiResponse<T>;

  if (!res.ok) {
    throw new Error(body.message ?? `Request failed (${res.status})`);
  }

  return body;
}
```
