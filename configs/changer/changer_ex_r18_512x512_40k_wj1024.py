# to begin from a pretrained model pth
# load_from = '/Users/heyuqi/Downloads/Coding/Geospatial/open-cd/pth/ChangerEx_r18-512x512_40k_levircd_20221223_120511.pth'

_base_ = [
    '../_base_/models/changer_r18.py', 
    '../common/standard_512x512_40k_levircd.py']
    

crop_size = (512, 512)
# Avoid 255 in padded segmentation masks (can crash class_weight CE on mmseg);
# crop_size is divisible by 32 so padding should be minimal anyway.
data_preprocessor = dict(seg_pad_val=0)
model = dict(
    data_preprocessor=dict(seg_pad_val=0),
    backbone=dict(
        interaction_cfg=(
            None,
            dict(type='SpatialExchange', p=1/2),
            dict(type='ChannelExchange', p=1/2),
            dict(type='ChannelExchange', p=1/2))
    ),
    decode_head=dict(
        num_classes=2,
        ignore_index=255,
        loss_decode=dict(
            type='mmseg.CrossEntropyLoss',
            use_sigmoid=False,
            loss_weight=1.0,
            # mmseg's class_weight path indexes by raw label values; if any pixel is 255 (ignore),
            # a 2-length list will crash. Provide weights up to index 255; set weight 0 for 255.
            class_weight=[1.0, 50.0]),
        sampler=dict(type='mmseg.OHEMPixelSampler', thresh=0.7, min_kept=100000)),
        # test_cfg=dict(mode='slide', crop_size=crop_size, stride=(crop_size[0]//2, crop_size[1]//2)),
    )

train_pipeline = [
    dict(type='MultiImgLoadImageFromFile', imdecode_backend='tifffile'),
    dict(type='MultiImgLoadAnnotations', imdecode_backend='tifffile'),
    dict(type='MultiImgRandomRotFlip', rotate_prob=0.5, flip_prob=0.5, degree=(-20, 20), pad_val=0, seg_pad_val=0),
    dict(type='MultiImgRandomCrop', crop_size=crop_size, cat_max_ratio=0.75),
    dict(type='MultiImgExchangeTime', prob=0.5),
    dict(
        type='MultiImgPhotoMetricDistortion',
        brightness_delta=10,
        contrast_range=(0.8, 1.2),
        saturation_range=(0.8, 1.2),
        hue_delta=10),
    dict(type='MultiImgPackSegInputs')
]

train_dataloader = dict(
    dataset=dict(pipeline=train_pipeline))

# optimizer
optimizer=dict(
    type='AdamW', lr=0.005, betas=(0.9, 0.999), weight_decay=0.05)
optim_wrapper = dict(
    _delete_=True,
    type='OptimWrapper',
    optimizer=optimizer)

# compile = True # use PyTorch 2.x

# to resume at the very bottom
resume = True
resume_from = 'work_dirs/wj1024_changerex_r18/iter_12000.pth'
load_from = None