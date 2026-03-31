# Tomb Raider II Level Parser & Viewer

Bu proje, efsanevi **Tomb Raider II (1997)** oyununun `.TR2` uzantılı bölüm (level) dosyalarını okumak, ayrıştırmak ve **OpenTK** (OpenGL) kullanarak 3D ortamda görselleştirmek için geliştirilmiş bir C# projesidir.

Projenin temel amacı, orijinal oyun motorunun kullandığı ikili (binary) veri yapılarını modern bir nesne modeline aktarmak ve bu verileri GPU üzerinde yeniden oluşturmaktır.

## 🚀 Özellikler (Şu Anki Durum)

Projenin merkezinde yer alan `TR2Level.cs` sınıfı, standart TR2 spesifikasyonlarına göre aşağıdaki verileri başarıyla ayrıştırabilmektedir:

* **Oda Geometrisi (Rooms):** Odaların köşe (vertex), dörtgen (quad) ve üçgen (triangle) verileri.
* **Dokular (Textures):** 8-bit ve 16-bit doku sayfaları (Textiles) ve nesnelerin UV koordinat eşleştirmeleri.
* **Modeller ve Statik Objeler:** Lara, düşmanlar, kapılar ve odalardaki sabit dekoratif objelerin veri yapıları.
* **Işıklandırma (Lighting):** Vertex tabanlı ışıklandırma ve odaların ortam (ambient) ışık değerleri.
* **Animasyon ve Yapay Zeka:** İskelet sistemleri, durum değişiklikleri ve AI navigasyon kutuları (Boxes, Overlaps).
* **Ses Haritalaması:** Dahili ses indeksleri ve örnekleme detayları.

## 🛠️ Teknolojiler

* **Programlama Dili:** C#
* **Grafik Kütüphanesi:** OpenTK (OpenGL)
* **Hedef Platform:** .NET 

## ⚙️ Nasıl Çalışır?

`TR2Level` sınıfı, bir `.TR2` dosyasını parametre olarak alır ve `BinaryReader` kullanarak tüm ikili yapıyı belleğe yükler. Değişken uzunluklu geometri verileri (Geometry Word List) işaretçiler (pointer) aracılığıyla taranarak kullanılabilir listelere dönüştürülür.

## 📜 Lisans
Bu proje eğitim ve araştırma amaçlı geliştirilmiştir. Tomb Raider ve ilgili tüm materyallerin hakları ilgili sahiplerine aittir.

**Örnek Kullanım:**
```csharp
// Bölüm dosyasını yükle ve ayrıştır
 static void Main()
 {
     string filePath = "levels/wall.TR2";
     ...
 }


