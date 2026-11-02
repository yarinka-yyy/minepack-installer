import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.zip.*;
import org.objectweb.asm.*;

class Build {
    static final String INPUT_SHA512 = "62f91af951f8f1a9a6e0fb75f133622202cc75292fc0e38d61eefd6b5a0904860180bb46fd11ea4110dc8b30da83edf055ee04f905d5351e2aedbde6c84737b1";
    static final String MIXIN = "org/tastytrash/spatialGUI/mixin/client/MinecraftMixin.class";
    static int fixes;

    static byte[] patch(byte[] original) {
        ClassWriter writer = new ClassWriter(0);
        new ClassReader(original).accept(new ClassVisitor(Opcodes.ASM9, writer) {
            @Override public MethodVisitor visitMethod(int access, String name, String descriptor, String signature, String[] exceptions) {
                MethodVisitor method = super.visitMethod(access, name, descriptor, signature, exceptions);
                if (!name.equals("spatialGUI$onPanoramaCaptureEnd")) return method;
                return new MethodVisitor(Opcodes.ASM9, method) {
                    @Override public AnnotationVisitor visitAnnotation(String descriptor, boolean visible) {
                        AnnotationVisitor annotation = super.visitAnnotation(descriptor, visible);
                        if (!descriptor.equals("Lorg/spongepowered/asm/mixin/injection/Inject;")) return annotation;
                        return new AnnotationVisitor(Opcodes.ASM9, annotation) {
                            @Override public AnnotationVisitor visitArray(String name) {
                                AnnotationVisitor array = super.visitArray(name);
                                if (!name.equals("at")) return array;
                                return new AnnotationVisitor(Opcodes.ASM9, array) {
                                    @Override public AnnotationVisitor visitAnnotation(String name, String descriptor) {
                                        AnnotationVisitor at = super.visitAnnotation(name, descriptor);
                                        if (!descriptor.equals("Lorg/spongepowered/asm/mixin/injection/At;")) return at;
                                        return new AnnotationVisitor(Opcodes.ASM9, at) {
                                            @Override public void visit(String name, Object value) {
                                                if (name.equals("value") && value.equals("TAIL")) { value = "RETURN"; fixes++; }
                                                super.visit(name, value);
                                            }
                                        };
                                    }
                                };
                            }
                        };
                    }
                };
            }
        }, 0);
        if (fixes != 1) throw new IllegalStateException("Expected exactly one upstream panorama fix; found " + fixes);
        return writer.toByteArray();
    }

    static void put(ZipOutputStream zip, String name, byte[] bytes) throws IOException {
        ZipEntry entry = new ZipEntry(name);
        entry.setTime(946684800000L);
        zip.putNextEntry(entry);
        zip.write(bytes);
        zip.closeEntry();
    }

    public static void main(String[] args) throws Exception {
        Path input = Path.of(args[0]), output = Path.of(args[1]), license = Path.of(args[2]);
        String hash = HexFormat.of().formatHex(MessageDigest.getInstance("SHA-512").digest(Files.readAllBytes(input)));
        if (!hash.equals(INPUT_SHA512)) throw new IllegalArgumentException("Unexpected upstream JAR hash");
        Map<String, byte[]> files = new TreeMap<>();
        try (ZipFile zip = new ZipFile(input.toFile())) {
            for (ZipEntry entry : Collections.list(zip.entries())) if (!entry.isDirectory())
                files.put(entry.getName(), zip.getInputStream(entry).readAllBytes());
        }
        files.put(MIXIN, patch(files.get(MIXIN)));
        String metadata = new String(files.get("fabric.mod.json"), StandardCharsets.UTF_8);
        String updated = metadata.replaceFirst("\"version\"\\s*:\\s*\"1\\.9\\.1\"", "\"version\": \"1.9.1+minepack.1\"");
        if (updated.equals(metadata)) throw new IllegalStateException("Unexpected upstream version");
        files.put("fabric.mod.json", updated.getBytes(StandardCharsets.UTF_8));
        files.put("LICENSE-MinePack-SpatialGUI.txt", Files.readAllBytes(license));
        files.put("MinePack-panorama-fix.txt", ("Spatial GUI 1.9.1: backport only the panorama TAIL -> RETURN fix from upstream commit " +
            "a1ba5b8aaf9499ff4f503ad9e8ec2dac4e71583d.\nhttps://github.com/tastytrash/Spatial-GUI/issues/173\n" +
            "No other features from that commit are included. Rebuild sources: third-party/spatial-gui-panorama-fix.\n").getBytes(StandardCharsets.UTF_8));
        try (ZipOutputStream zip = new ZipOutputStream(Files.newOutputStream(output, StandardOpenOption.CREATE_NEW))) {
            for (var file : files.entrySet()) put(zip, file.getKey(), file.getValue());
        }
        System.out.println("PASS: one panorama annotation fixed; upstream content preserved except mixin/version and two added notices.");
    }
}
