plugins {
    id("com.android.application")
}

val legalResourcesDirectory = layout.buildDirectory.get().dir("generated/legal-res").asFile
val legalFiles = mapOf(
    "../LICENSE" to "project_license.txt",
    "../THIRD_PARTY_NOTICES.md" to "third_party_notices.txt",
    "../SOURCE_OFFER.md" to "source_offer.txt",
    "../licenses/Apache-2.0.txt" to "apache_2_0.txt",
    "../licenses/LGPL-2.1.txt" to "lgpl_2_1.txt",
    "../licenses/MIT-components.txt" to "mit_components.txt",
    "../licenses/SDL-zlib.txt" to "sdl_zlib.txt",
    "../licenses/dav1d-BSD-2-Clause.txt" to "dav1d_bsd_2_clause.txt",
)

val generateLegalResources by tasks.registering(Copy::class) {
    into(legalResourcesDirectory.resolve("raw"))
    legalFiles.forEach { (source, target) ->
        from(rootProject.file(source)) { rename { target } }
    }
}

val releaseStorePath = providers.environmentVariable("DEVICE_WIDGET_ANDROID_KEYSTORE").orNull
val releaseStorePassword = providers.environmentVariable("DEVICE_WIDGET_ANDROID_STORE_PASSWORD").orNull
val releaseKeyAlias = providers.environmentVariable("DEVICE_WIDGET_ANDROID_KEY_ALIAS").orNull
val releaseKeyPassword = providers.environmentVariable("DEVICE_WIDGET_ANDROID_KEY_PASSWORD").orNull
val hasReleaseSigning = listOf(
    releaseStorePath,
    releaseStorePassword,
    releaseKeyAlias,
    releaseKeyPassword,
).all { !it.isNullOrBlank() }

android {
    namespace = "dev.androidwidget.companion"
    compileSdk = 36

    defaultConfig {
        applicationId = "dev.androidwidget.companion"
        minSdk = 26
        targetSdk = 36
        versionCode = 2
        versionName = "0.1.1"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        buildConfig = true
    }

    sourceSets["main"].res.srcDir(legalResourcesDirectory)

    signingConfigs {
        if (hasReleaseSigning) {
            create("release") {
                storeFile = file(requireNotNull(releaseStorePath))
                storePassword = requireNotNull(releaseStorePassword)
                keyAlias = requireNotNull(releaseKeyAlias)
                keyPassword = requireNotNull(releaseKeyPassword)
            }
        }
    }

    buildTypes {
        getByName("release") {
            if (hasReleaseSigning)
                signingConfig = signingConfigs.getByName("release")
        }
    }
}

tasks.named("preBuild").configure {
    dependsOn(generateLegalResources)
}

dependencies {
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
}
