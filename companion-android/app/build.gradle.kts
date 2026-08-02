plugins {
    id("com.android.application")
}

android {
    namespace = "dev.androidwidget.companion"
    compileSdk = 36

    defaultConfig {
        applicationId = "dev.androidwidget.companion"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        buildConfig = true
    }
}

dependencies {
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
}
