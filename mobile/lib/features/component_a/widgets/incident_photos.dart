import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../models/incident.dart';

/// The photos on a report, swipeable, with a count. Tapping one opens it full
/// screen — the photo is often what tells someone how bad it really is.
class IncidentPhotoGallery extends StatefulWidget {
  const IncidentPhotoGallery({super.key, required this.photos, this.height = 220});

  final List<IncidentPhoto> photos;
  final double height;

  @override
  State<IncidentPhotoGallery> createState() => _IncidentPhotoGalleryState();
}

class _IncidentPhotoGalleryState extends State<IncidentPhotoGallery> {
  int _page = 0;

  @override
  Widget build(BuildContext context) {
    final photos = widget.photos;

    return ClipRRect(
      borderRadius: BorderRadius.circular(16),
      child: SizedBox(
        height: widget.height,
        child: Stack(
          children: [
            PageView.builder(
              itemCount: photos.length,
              onPageChanged: (page) => setState(() => _page = page),
              itemBuilder: (context, index) => GestureDetector(
                onTap: () => PhotoViewer.open(context, photos, index),
                child: Hero(
                  tag: 'incident-photo-${photos[index].id}',
                  child: IncidentPhotoImage(photo: photos[index]),
                ),
              ),
            ),
            Positioned(
              right: 10,
              bottom: 10,
              child: IgnorePointer(
                child: _Badge(
                  icon: Icons.zoom_out_map,
                  text: photos.length > 1
                      ? '${_page + 1} / ${photos.length}'
                      : 'Tap to enlarge',
                ),
              ),
            ),
            if (photos.length > 1)
              Positioned(
                left: 0,
                right: 0,
                bottom: 12,
                child: IgnorePointer(
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      for (var i = 0; i < photos.length; i++)
                        AnimatedContainer(
                          duration: const Duration(milliseconds: 200),
                          margin: const EdgeInsets.symmetric(horizontal: 3),
                          width: i == _page ? 16 : 6,
                          height: 6,
                          decoration: BoxDecoration(
                            color: i == _page ? Colors.white : Colors.white60,
                            borderRadius: BorderRadius.circular(99),
                          ),
                        ),
                    ],
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// One photo from the network, with a placeholder while it loads and a clear
/// message — not a blank box — when it cannot.
class IncidentPhotoImage extends StatelessWidget {
  const IncidentPhotoImage({
    super.key,
    required this.photo,
    this.fit = BoxFit.cover,
    this.dark = false,
  });

  final IncidentPhoto photo;
  final BoxFit fit;

  /// For the black full-screen viewer.
  final bool dark;

  @override
  Widget build(BuildContext context) {
    final background = dark ? Colors.black : AppColors.surfaceAlt;
    final ink = dark ? Colors.white70 : AppColors.body;

    return Image.network(
      photo.resolvedUrl,
      fit: fit,
      width: double.infinity,
      height: double.infinity,
      loadingBuilder: (context, child, progress) {
        if (progress == null) return child;
        final total = progress.expectedTotalBytes;
        return ColoredBox(
          color: background,
          child: Center(
            child: SizedBox(
              width: 26,
              height: 26,
              child: CircularProgressIndicator(
                strokeWidth: 2.4,
                value: total == null ? null : progress.cumulativeBytesLoaded / total,
              ),
            ),
          ),
        );
      },
      errorBuilder: (context, error, stackTrace) => ColoredBox(
        color: background,
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.broken_image_outlined, size: 30, color: ink),
              const SizedBox(height: 6),
              Text(
                'Photo could not load',
                style: TextStyle(fontSize: 12, color: ink),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// A small square thumbnail for list rows.
class IncidentPhotoThumb extends StatelessWidget {
  const IncidentPhotoThumb({super.key, required this.photo, this.size = 44});

  final IncidentPhoto photo;
  final double size;

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(12),
      child: SizedBox(
        width: size,
        height: size,
        child: Image.network(
          photo.resolvedUrl,
          fit: BoxFit.cover,
          // A thumbnail never needs the full-size bytes decoded.
          cacheWidth: (size * MediaQuery.devicePixelRatioOf(context)).round(),
          errorBuilder: (context, error, stackTrace) => const ColoredBox(
            color: AppColors.surfaceAlt,
            child: Icon(Icons.image_outlined, size: 20, color: AppColors.body),
          ),
        ),
      ),
    );
  }
}

/// Full-screen photos: swipe between them, pinch or double-tap to zoom.
class PhotoViewer extends StatefulWidget {
  const PhotoViewer({super.key, required this.photos, required this.initialIndex});

  final List<IncidentPhoto> photos;
  final int initialIndex;

  static Future<void> open(
    BuildContext context,
    List<IncidentPhoto> photos,
    int index,
  ) {
    return Navigator.of(context, rootNavigator: true).push(
      PageRouteBuilder<void>(
        opaque: false,
        barrierColor: Colors.black,
        pageBuilder: (context, animation, secondaryAnimation) =>
            PhotoViewer(photos: photos, initialIndex: index),
        transitionsBuilder: (context, animation, secondaryAnimation, child) =>
            FadeTransition(opacity: animation, child: child),
      ),
    );
  }

  @override
  State<PhotoViewer> createState() => _PhotoViewerState();
}

class _PhotoViewerState extends State<PhotoViewer> {
  late final PageController _pages = PageController(initialPage: widget.initialIndex);
  late int _page = widget.initialIndex;

  @override
  void dispose() {
    _pages.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final photo = widget.photos[_page];

    return Scaffold(
      backgroundColor: Colors.black,
      body: Stack(
        children: [
          PageView.builder(
            controller: _pages,
            itemCount: widget.photos.length,
            onPageChanged: (page) => setState(() => _page = page),
            itemBuilder: (context, index) => _ZoomablePhoto(
              photo: widget.photos[index],
            ),
          ),
          SafeArea(
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
              child: Row(
                children: [
                  IconButton(
                    onPressed: () => Navigator.of(context).pop(),
                    icon: const Icon(Icons.close, color: Colors.white),
                    style: IconButton.styleFrom(backgroundColor: Colors.black45),
                    tooltip: 'Close',
                  ),
                  const Spacer(),
                  if (widget.photos.length > 1)
                    _Badge(text: '${_page + 1} / ${widget.photos.length}'),
                ],
              ),
            ),
          ),
          if (photo.caption != null && photo.caption!.trim().isNotEmpty)
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: Container(
                padding: EdgeInsets.fromLTRB(
                  20,
                  16,
                  20,
                  16 + MediaQuery.paddingOf(context).bottom,
                ),
                color: Colors.black54,
                child: Text(
                  photo.caption!,
                  style: const TextStyle(color: Colors.white, fontSize: 14, height: 1.4),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class _ZoomablePhoto extends StatefulWidget {
  const _ZoomablePhoto({required this.photo});

  final IncidentPhoto photo;

  @override
  State<_ZoomablePhoto> createState() => _ZoomablePhotoState();
}

class _ZoomablePhotoState extends State<_ZoomablePhoto> {
  final _zoom = TransformationController();
  TapDownDetails? _doubleTap;

  @override
  void dispose() {
    _zoom.dispose();
    super.dispose();
  }

  /// Double-tap zooms in on the spot tapped, and back out again.
  void _toggleZoom() {
    if (_zoom.value != Matrix4.identity()) {
      _zoom.value = Matrix4.identity();
      return;
    }
    final point = _doubleTap?.localPosition ?? Offset.zero;
    const scale = 2.5;
    _zoom.value = Matrix4.identity()
      ..translateByDouble(-point.dx * (scale - 1), -point.dy * (scale - 1), 0, 1)
      ..scaleByDouble(scale, scale, 1, 1);
  }

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onDoubleTapDown: (details) => _doubleTap = details,
      onDoubleTap: _toggleZoom,
      child: InteractiveViewer(
        transformationController: _zoom,
        minScale: 1,
        maxScale: 5,
        child: Center(
          child: Hero(
            tag: 'incident-photo-${widget.photo.id}',
            child: IncidentPhotoImage(
              photo: widget.photo,
              fit: BoxFit.contain,
              dark: true,
            ),
          ),
        ),
      ),
    );
  }
}

class _Badge extends StatelessWidget {
  const _Badge({required this.text, this.icon});

  final String text;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
      decoration: BoxDecoration(
        color: Colors.black54,
        borderRadius: BorderRadius.circular(99),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 13, color: Colors.white),
            const SizedBox(width: 5),
          ],
          Text(
            text,
            style: const TextStyle(
              color: Colors.white,
              fontSize: 12,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}
