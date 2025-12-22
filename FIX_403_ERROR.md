# 🔧 راهنمای رفع خطای 403 در GitHub Actions Release

## مشکل
خطای `403` هنگام ایجاد Release در GitHub Actions:
```
⚠️ GitHub release failed with status: 403
```

## راه حل‌ها

### ✅ راه حل 1: بررسی تنظیمات Repository (مهم‌ترین)

1. به **Settings** → **Actions** → **General** بروید
2. در بخش **Workflow permissions**:
   - گزینه **"Read and write permissions"** را انتخاب کنید
   - یا **"Read repository contents and packages permissions"** را انتخاب کنید و تیک **"Allow GitHub Actions to create and approve pull requests"** را بزنید

3. روی **Save** کلیک کنید

### ✅ راه حل 2: بررسی Permissions در Workflow

مطمئن شوید که workflow شامل permissions است (این کار انجام شده است):

```yaml
permissions:
  contents: write
  pull-requests: read
```

### ✅ راه حل 3: بررسی Branch Protection Rules

اگر شاخه `publish` محافظت شده است:

1. به **Settings** → **Branches** بروید
2. اگر rule برای `publish` وجود دارد:
   - مطمئن شوید که **"Allow GitHub Actions to bypass branch protection"** فعال است
   - یا rule را موقتاً غیرفعال کنید

### ✅ راه حل 4: استفاده از Personal Access Token (PAT)

اگر مشکل ادامه داشت، می‌توانید از Personal Access Token استفاده کنید:

1. **ایجاد PAT:**
   - به **Settings** → **Developer settings** → **Personal access tokens** → **Tokens (classic)** بروید
   - روی **"Generate new token (classic)"** کلیک کنید
   - نامی برای token انتخاب کنید (مثلاً: `GitHub Actions Release`)
   - Scope های زیر را انتخاب کنید:
     - ✅ `repo` (Full control of private repositories)
     - ✅ `write:packages`
   - روی **"Generate token"** کلیک کنید
   - **Token را کپی کنید** (فقط یک بار نمایش داده می‌شود!)

2. **اضافه کردن به Secrets:**
   - به **Settings** → **Secrets and variables** → **Actions** بروید
   - روی **"New repository secret"** کلیک کنید
   - Name: `RELEASE_TOKEN`
   - Value: token که کپی کردید
   - روی **"Add secret"** کلیک کنید

3. **به‌روزرسانی Workflow:**
   در فایل `.github/workflows/release.yml`، خط `GITHUB_TOKEN` را تغییر دهید:
   ```yaml
   env:
     GITHUB_TOKEN: ${{ secrets.RELEASE_TOKEN }}
   ```

### ✅ راه حل 5: بررسی Organization Settings

اگر repository در یک Organization است:

1. به **Settings** → **Actions** → **General** بروید
2. مطمئن شوید که **"Allow GitHub Actions in this organization"** فعال است
3. در بخش **Workflow permissions**، **"Read and write permissions"** را انتخاب کنید

## بررسی‌های اضافی

### بررسی اینکه Tag قبلاً وجود ندارد

اگر tag قبلاً ایجاد شده باشد، ممکن است خطا رخ دهد. می‌توانید:

1. Tag های موجود را بررسی کنید:
   ```bash
   git tag -l
   ```

2. Tag قدیمی را حذف کنید (در صورت نیاز):
   ```bash
   git tag -d v1.0.0-0fe9cf7
   git push origin :refs/tags/v1.0.0-0fe9cf7
   ```

### بررسی لاگ‌های Workflow

1. به تب **Actions** بروید
2. روی workflow اجرا شده کلیک کنید
3. لاگ step **"Create Release"** را بررسی کنید
4. پیام خطای کامل را بخوانید

## تست بعد از رفع مشکل

بعد از اعمال تغییرات:

1. تغییرات را commit کنید:
   ```bash
   git add .github/workflows/release.yml
   git commit -m "Fix: Add permissions for release workflow"
   git push origin publish
   ```

2. workflow را در تب **Actions** مشاهده کنید

3. اگر موفق بود، Release را در تب **Releases** بررسی کنید

## اگر مشکل ادامه داشت

1. **بررسی کنید که آیا repository public است یا private:**
   - برای private repositories، ممکن است نیاز به PAT باشد

2. **بررسی کنید که آیا از Fork استفاده می‌کنید:**
   - Fork ها ممکن است محدودیت‌های خاصی داشته باشند

3. **تماس با GitHub Support:**
   - اگر هیچکدام از راه حل‌ها کار نکرد، با GitHub Support تماس بگیرید

## خلاصه تغییرات انجام شده

✅ Permissions به workflow اضافه شد:
```yaml
permissions:
  contents: write
  pull-requests: read
```

✅ Checkout با token تنظیم شد:
```yaml
- name: Checkout code
  uses: actions/checkout@v4
  with:
    fetch-depth: 0
    token: ${{ secrets.GITHUB_TOKEN }}
```

این تغییرات باید مشکل 403 را حل کند. اگر مشکل ادامه داشت، از راه حل 4 (استفاده از PAT) استفاده کنید.

